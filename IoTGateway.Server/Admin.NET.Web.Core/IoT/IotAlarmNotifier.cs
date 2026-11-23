using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Admin.NET.Web.Core;

/// <summary>
/// 邮件、钉钉和飞书告警通知发送器
/// </summary>
public class IotAlarmNotifier : IIotAlarmNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;

    public IotAlarmNotifier(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IotAlarmNotificationResult> SendAsync(
        IotAlarmChannel channel,
        IotAlarmEvent alarmEvent,
        IotAlarmNotificationTypeEnum notificationType,
        CancellationToken cancellationToken = default)
    {
        var content = BuildContent(alarmEvent, notificationType);
        try
        {
            return channel.Type switch
            {
                IotAlarmChannelTypeEnum.Email => await SendEmailAsync(channel, alarmEvent, notificationType, content, cancellationToken),
                IotAlarmChannelTypeEnum.DingTalk => await SendDingTalkAsync(channel, content, cancellationToken),
                IotAlarmChannelTypeEnum.Feishu => await SendFeishuAsync(channel, content, cancellationToken),
                _ => new IotAlarmNotificationResult { IsSuccess = false, Content = content, Response = "不支持的通知渠道" }
            };
        }
        catch (Exception ex)
        {
            return new IotAlarmNotificationResult { IsSuccess = false, Content = content, Response = ex.Message };
        }
    }

    private static async Task<IotAlarmNotificationResult> SendEmailAsync(
        IotAlarmChannel channel,
        IotAlarmEvent alarmEvent,
        IotAlarmNotificationTypeEnum type,
        string content,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(channel.Server) || channel.Port is not > 0)
            return Failure(content, "SMTP服务器或端口未配置");
        if (string.IsNullOrWhiteSpace(channel.Sender) || string.IsNullOrWhiteSpace(channel.Recipients))
            return Failure(content, "发件人或收件人未配置");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(channel.Sender));
        var recipients = InternetAddressList.Parse(channel.Recipients.Replace(';', ','));
        message.To.AddRange(recipients);
        message.Subject = $"[IoT {TypeName(type)}] {alarmEvent.RuleName} - {alarmEvent.DeviceName}";
        message.Body = new BodyBuilder
        {
            TextBody = content,
            HtmlBody = $"<html><body><h3>{WebUtility.HtmlEncode(message.Subject)}</h3><pre style=\"white-space:pre-wrap;font-family:inherit\">{WebUtility.HtmlEncode(content)}</pre></body></html>"
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15000 };
        var secure = channel.UseSsl
            ? channel.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;
        await client.ConnectAsync(channel.Server, channel.Port.Value, secure, token);
        if (!string.IsNullOrWhiteSpace(channel.Username))
            await client.AuthenticateAsync(channel.Username, channel.Secret ?? string.Empty, token);
        var response = await client.SendAsync(message, token);
        await client.DisconnectAsync(true, token);
        return new IotAlarmNotificationResult { IsSuccess = true, Content = content, Response = response };
    }

    private async Task<IotAlarmNotificationResult> SendDingTalkAsync(IotAlarmChannel channel, string content, CancellationToken token)
    {
        if (!Uri.TryCreate(channel.WebhookUrl, UriKind.Absolute, out var uri)) return Failure(content, "钉钉 Webhook 地址无效");
        var url = channel.WebhookUrl;
        if (!string.IsNullOrWhiteSpace(channel.Secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var sign = HttpUtilityUrlEncode(HmacSha256(channel.Secret, $"{timestamp}\n{channel.Secret}"));
            url += (url.Contains('?') ? "&" : "?") + $"timestamp={timestamp}&sign={sign}";
        }

        var payload = JsonSerializer.Serialize(new
        {
            msgtype = "text",
            text = new { content }
        });
        return await PostWebhookAsync(url, payload, content, token);
    }

    private async Task<IotAlarmNotificationResult> SendFeishuAsync(IotAlarmChannel channel, string content, CancellationToken token)
    {
        if (!Uri.TryCreate(channel.WebhookUrl, UriKind.Absolute, out _)) return Failure(content, "飞书 Webhook 地址无效");
        string timestamp = null;
        string sign = null;
        if (!string.IsNullOrWhiteSpace(channel.Secret))
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes($"{timestamp}\n{channel.Secret}"));
            sign = Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
        }

        var payload = JsonSerializer.Serialize(new
        {
            timestamp,
            sign,
            msg_type = "text",
            content = new { text = content }
        });
        return await PostWebhookAsync(channel.WebhookUrl, payload, content, token);
    }

    private async Task<IotAlarmNotificationResult> PostWebhookAsync(string url, string payload, string content, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var client = _httpClientFactory.CreateClient(nameof(IotAlarmNotifier));
        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, body, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode) return Failure(content, $"HTTP {(int)response.StatusCode}：{responseText}");
        if (WebhookRejected(responseText, out var error)) return Failure(content, error);
        return new IotAlarmNotificationResult { IsSuccess = true, Content = content, Response = responseText };
    }

    private static bool WebhookRejected(string response, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            foreach (var name in new[] { "errcode", "code", "StatusCode" })
            {
                if (!root.TryGetProperty(name, out var code)) continue;
                var value = code.ValueKind == JsonValueKind.Number ? code.GetInt64() : long.TryParse(code.GetString(), out var parsed) ? parsed : 0;
                if (value == 0) continue;
                var message = root.TryGetProperty("errmsg", out var errmsg) ? errmsg.GetString()
                    : root.TryGetProperty("msg", out var msg) ? msg.GetString()
                    : root.TryGetProperty("StatusMessage", out var statusMessage) ? statusMessage.GetString() : response;
                error = $"Webhook 返回错误 {value}：{message}";
                return true;
            }
        }
        catch (JsonException)
        {
            // 部分测试 Webhook 返回纯文本，HTTP 成功即视为成功。
        }
        return false;
    }

    private static string BuildContent(IotAlarmEvent alarmEvent, IotAlarmNotificationTypeEnum type)
    {
        if (type == IotAlarmNotificationTypeEnum.Test)
            return $"IoT 告警通知渠道测试\n发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n如果您收到此消息，说明渠道配置有效。";

        return $"【{TypeName(type)}】{alarmEvent.RuleName}\n" +
               $"告警级别：{SeverityName(alarmEvent.Severity)}\n" +
               $"设备：{alarmEvent.DeviceName}\n" +
               $"事件状态：{alarmEvent.Status}\n" +
               $"首次触发：{alarmEvent.TriggerTime:yyyy-MM-dd HH:mm:ss}\n" +
               $"最近触发：{alarmEvent.LastTriggerTime:yyyy-MM-dd HH:mm:ss}\n" +
               (alarmEvent.RecoverTime.HasValue ? $"恢复时间：{alarmEvent.RecoverTime:yyyy-MM-dd HH:mm:ss}\n" : string.Empty) +
               $"触发详情：{alarmEvent.Message}\n" +
               $"事件Id：{alarmEvent.Id}";
    }

    private static string TypeName(IotAlarmNotificationTypeEnum type) => type switch
    {
        IotAlarmNotificationTypeEnum.Trigger => "告警触发",
        IotAlarmNotificationTypeEnum.Reminder => "重复提醒",
        IotAlarmNotificationTypeEnum.Recovery => "告警恢复",
        _ => "渠道测试"
    };

    private static string SeverityName(IotAlarmSeverityEnum severity) => severity switch
    {
        IotAlarmSeverityEnum.Info => "提示",
        IotAlarmSeverityEnum.Warning => "一般",
        IotAlarmSeverityEnum.Critical => "严重",
        _ => "紧急"
    };

    private static string HmacSha256(string key, string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string HttpUtilityUrlEncode(string value) => Uri.EscapeDataString(value).Replace("%20", "+");

    private static IotAlarmNotificationResult Failure(string content, string response) =>
        new() { IsSuccess = false, Content = content, Response = response };
}
