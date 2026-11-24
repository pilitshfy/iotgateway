# IoTGateway 集团级数采平台 · 实施与部署文档

> 适用版本：当前工程基线（2026-11-23，已完成 P0–P6 阶段）
> 文档范围：架构说明、环境准备、后端/前端部署、边缘接入协议、初始化、验收、运维与故障排查
> 适用对象：实施工程师、运维工程师、边缘端开发人员

---

## 目录

1. [系统概述](#1-系统概述)
2. [组件与技术栈](#2-组件与技术栈)
3. [部署规划](#3-部署规划)
4. [环境准备](#4-环境准备)
5. [后端部署](#5-后端部署)
6. [前端部署](#6-前端部署)
7. [系统初始化与配置](#7-系统初始化与配置)
8. [边缘节点接入协议](#8-边缘节点接入协议)
9. [时序存储设计](#9-时序存储设计)
10. [API 清单](#10-api-清单)
11. [运维手册](#11-运维手册)
12. [安全加固](#12-安全加固)
13. [验收清单](#13-验收清单)
14. [附录](#14-附录)

---

## 1. 系统概述

### 1.1 定位

IoTGateway 是面向集团的工业数据采集（数采）平台，采用「中心平台 + 边缘节点」分级架构：

- **中心平台**：组织/租户隔离、设备模型、驱动管理、时序存储、告警中心、报表 BI、开放 API。
- **边缘节点**：就近接入 PLC/CNC/仪表/OPC UA 等设备，按中心下发配置采集并上报。
- **双通道汇聚**：边缘数据可经 MQTT 或 Kafka 上报，中心以统一处理器解析后写入时序库与告警引擎。

### 1.2 总体架构

```
┌──────────────────────────── 中心平台（Admin.NET / .NET 8） ────────────────────────────┐
│  Vue3 前端（Element Plus + ECharts）                                                   │
│        │ HTTPS                                                                         │
│  Nginx ─┬─ /            → 前端静态资源                                                  │
│         ├─ /api/        → 后端 5005                                                     │
│         └─ /mqtt        → MQTT over WebSocket                                           │
│                                                                                        │
│  Admin.NET Web API（5005）                                                              │
│   ├─ 组织/用户/角色/菜单（RBAC，租户 1300000000001）                                     │
│   ├─ 设备/变量/驱动/通讯配置                                                             │
│   ├─ 采集运行时（驱动 → 内部 MQTT 1888 → 上游平台转发）                                   │
│   ├─ 边缘入站统一处理器 IotEdgeIngestProcessor                                           │
│   │    ├─ MQTT 通道：edge/{nodeCode}/heartbeat|telemetry                                │
│   │    └─ Kafka 通道：iot.edge.ingest（可选）                                            │
│   ├─ 时序写入 IotHistorySink（批量 COPY）                                                 │
│   ├─ 告警引擎 IotAlarmEngine（规则计算 + 邮件/钉钉/飞书通知）                              │
│   └─ 报表 BI / 历史查询 API                                                              │
│                                                                                        │
│  PostgreSQL 16                                                                          │
│   ├─ iotgateway      配置库（设备/变量/告警/系统表）                                      │
│   └─ iotgateway_ts   TimescaleDB（iot_metric 超表）                                      │
└──────────────────────────────────────────▲─────────────────────────────────────────────┘
                                           │ MQTT（TCP 1888）或 Kafka（9092）
              ┌────────────────────────────┼────────────────────────────┐
              ▼                            ▼                            ▼
      ┌─ 边缘节点 A ─┐             ┌─ 边缘节点 B ─┐             ┌─ 边缘节点 N ─┐
      │ 驱动采集      │             │ 驱动采集      │             │ 驱动采集      │
      │ 心跳/遥测上报  │             │ 心跳/遥测上报  │             │ 心跳/遥测上报  │
      │ 接收中心配置   │             │ 接收中心配置   │             │ 接收中心配置   │
      └───────────────┘             └───────────────┘             └───────────────┘
```

### 1.3 关键数据流

1. 驱动采集：本机 `IotDeviceRuntime` 按设备配置周期读取驱动，数据经内部 MQTT（1888）发布，并按需转发上游平台。
2. 边缘上报：边缘节点以 `edge/{nodeCode}/telemetry` 发布遥测、`edge/{nodeCode}/heartbeat` 发布心跳。
3. 入站解析：MQTT / Kafka 通道把消息还原为 `(nodeCode, kind, payload)`，交给 `IotEdgeIngestProcessor` 统一处理。
4. 落库与告警：遥测写入 `iot_metric`（批量、异步、有界队列），同时进入告警引擎计算；心跳/遥测更新节点在线状态。
5. 查询展示：历史趋势与报表 BI 直接查询 TimescaleDB，支持原始值与 `time_bucket` 聚合。

### 1.4 已实现能力清单

| 阶段 | 能力 | 状态 |
|---|---|---|
| P0 | PostgreSQL 配置库、组织模型、边缘节点模型、菜单种子 | 已实现 |
| P1 | TimescaleDB 时序存储、批量写入、历史查询、保留策略 | 已实现 |
| P2 | 组织数据隔离（设备/变量/历史/告警按组织过滤） | 已实现 |
| P3 | 边缘节点注册/心跳/在线检测、配置与指令下发 | 已实现 |
| P4 | 告警规则/事件/通知渠道/告警大屏 | 已实现 |
| P5 | 报表 BI（变量 × 聚合 × 粒度，表格 + 图表 + CSV 导出） | 已实现 |
| P6 | Kafka 双通道入站（与 MQTT 并列）、可选出向转发 | 已实现 |
| — | 心跳/遥测接入密钥鉴权 | 已实现 |

---

## 2. 组件与技术栈

| 组件 | 版本/选型 | 说明 |
|---|---|---|
| 操作系统 | Ubuntu 22.04 / 24.04 LTS（推荐） | Windows Server 亦可（后端支持 Windows 服务） |
| .NET Runtime | 8.0 | 后端运行时；构建需要 .NET 8 SDK |
| 后端框架 | Admin.NET / Furion 4.9.9.92 | 动态 API、RBAC、多租户、CodeFirst |
| MQTT Broker | MQTTnet 4.3.7（内嵌） | 随后端启动，TCP 1888 + WebSocket `/mqtt` |
| Kafka 客户端 | Confluent.Kafka 2.5.3 | 可选通道 |
| Kafka Broker | Apache Kafka 4.4.0（KRaft 单节点） | 可选；已验证与 2.5.3 客户端兼容 |
| 配置数据库 | PostgreSQL 16 | 普通表存配置 |
| 时序数据库 | TimescaleDB 2.x（PG16 扩展） | `iot_metric` 超表 |
| 前端 | Vue 3 + Vite + Element Plus 2.14 + ECharts 6 | 构建产物为静态文件 |
| 前端包管理 | pnpm 10（Node 24） | 也兼容 npm |
| 反向代理 | Nginx 1.24+ | SPA、API、WebSocket 代理 |

> 驱动插件随程序发布到 `drivers/net8.0/`，包含 Modbus、Siemens S7、Melsec MC、Omron FINS、OPC UA/DA、MTConnect、Fanuc、Toledo 等。部分驱动（如 HslCommunication）为商业授权组件，部署前须确认授权合规。

---

## 3. 部署规划

### 3.1 服务器规格建议

| 场景 | 规模参考 | CPU/内存 | 磁盘 |
|---|---|---|---|
| 试点/单厂 | ≤ 100 设备，≤ 5,000 变量 | 4C / 8G | 200 GB SSD |
| 集团级标准 | ≤ 1,000 设备，≤ 50,000 变量 | 8C / 16G | 1 TB SSD |
| 大型集团 | 多实例 + Kafka + 独立数据库 | 按压测结果横向扩展 | 按留存周期估算 |

时序存储估算：

```
每天行数 = 变量数 × 86400 / 采集周期(秒)
磁盘 ≈ 每天行数 × (100~200 字节) × 留存天数
例：10,000 变量、60 秒采集、留存 30 天
   ≈ 10,000 × 1440 × 150B × 30 ≈ 60~65 GB
```

### 3.2 端口清单

| 端口 | 协议 | 用途 | 暴露范围 |
|---|---|---|---|
| 5005 | HTTP / WebSocket | 后端 API、`/mqtt`、`/sapi`、`/mcp` | 仅 Nginx/内网 |
| 1888 | MQTT TCP | 内嵌 Broker（边缘接入、内部采集） | 边缘网络/内网 |
| 5432 | PostgreSQL | 配置库 + 时序库 | 仅本机或数据库子网 |
| 9092 | Kafka PLAINTEXT | Broker（可选） | 仅内网 |
| 9093 | Kafka Controller | KRaft 控制器（可选） | 仅本机 |
| 80 / 443 | HTTP / HTTPS | Nginx 入口 | 用户网络 |
| 8888 | HTTP | 前端开发服务器（仅开发） | 开发机 |

### 3.3 目录规划（Linux 示例）

```
/opt/iotgateway/                 后端发布目录（含 Configuration、drivers、wwwroot）
/etc/systemd/system/iotgateway.service
/var/www/iotgateway/             前端 dist
/var/log/iotgateway/             应用日志（Admin.NET logs 目录）
/data/kafka-logs/                Kafka 数据（可选）
/opt/kafka/                      Kafka 程序（可选）
```

### 3.4 网络与安全域

- 用户区 → Nginx（443）：仅开放 Web 入口。
- 边缘区 → 平台（1888 或 Kafka 9092）：按节点 IP 白名单放行。
- 数据库/PG、Kafka Controller：不对外暴露。
- 当前内嵌 MQTT Broker 仅做连接放行，依赖边缘载荷中的 `accessKey` 做业务鉴权；**不要将 1888 直接暴露到公网**（详见第 12 章）。

---

## 4. 环境准备

### 4.1 操作系统基础配置

```bash
# 时区统一为 Asia/Shanghai（数据库内部按 UTC 存储，展示按本地时区）
sudo timedatectl set-timezone Asia/Shanghai

# 文件句柄与进程限制
cat <<'EOF' | sudo tee /etc/security/limits.d/iotgateway.conf
iotgateway soft nofile 65535
iotgateway hard nofile 65535
EOF

# 防火墙（按需）
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow from <边缘网段> to any port 1888 proto tcp
```

### 4.2 安装 .NET 8

```bash
# 生产仅需运行时；构建机安装 SDK
# 若系统尚未添加 Microsoft 包源，先按官方文档添加 .NET 8 源
sudo apt-get update
sudo apt-get install -y dotnet-runtime-8.0 aspnetcore-runtime-8.0
# 构建机：
# sudo apt-get install -y dotnet-sdk-8.0
dotnet --info
```

### 4.3 安装 PostgreSQL 16

```bash
sudo apt-get install -y postgresql-16 postgresql-client-16
sudo systemctl enable --now postgresql

# 建议：会话时区 UTC，避免 timestamptz 偏移
sudo -u postgres psql -c "ALTER SYSTEM SET timezone='UTC';"
sudo systemctl restart postgresql
```

### 4.4 安装 TimescaleDB 扩展

```bash
# 以 Ubuntu 24.04 为例（其他发行版参考 Timescale 官方仓库）
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://packagecloud.io/timescale/timescaledb/gpgkey \
  | sudo gpg --dearmor -o /etc/apt/keyrings/timescaledb.gpg
echo "deb [signed-by=/etc/apt/keyrings/timescaledb.gpg] https://packagecloud.io/timescale/timescaledb/ubuntu/ $(lsb_release -cs) main" \
  | sudo tee /etc/apt/sources.list.d/timescaledb.list
sudo apt-get update
sudo apt-get install -y timescaledb-2-postgresql-16
# 如已安装 timescaledb-tools 可执行自动调优；否则手动在 postgresql.conf 设置
# shared_preload_libraries = 'timescaledb'
sudo timescaledb-tune --yes || true
sudo systemctl restart postgresql

# 确认 preload 生效
sudo -u postgres psql -c "SHOW shared_preload_libraries;"
# 期望包含 timescaledb
```

### 4.5 创建数据库与账号

```bash
sudo -u postgres psql <<'SQL'
CREATE ROLE iotgateway LOGIN PASSWORD '请替换为强密码';
CREATE DATABASE iotgateway    OWNER iotgateway;
CREATE DATABASE iotgateway_ts OWNER iotgateway;
SQL

# TimescaleDB 扩展需要较高权限，建议由 postgres 预创建（应用启动时也会尝试创建）
sudo -u postgres psql -d iotgateway    -c "CREATE EXTENSION IF NOT EXISTS timescaledb;"
sudo -u postgres psql -d iotgateway_ts -c "CREATE EXTENSION IF NOT EXISTS timescaledb;"
```

> 说明：配置库表结构与种子数据由后端 CodeFirst 在首次启动时自动创建；时序表 `iot_metric`、超表、索引与保留策略由 `IotHistorySink` 启动时自动创建。

### 4.6 安装 Kafka（可选通道）

```bash
# 使用国内镜像下载（示例）
cd /tmp
wget https://mirrors.tuna.tsinghua.edu.cn/apache/kafka/4.4.0/kafka_2.13-4.4.0.tgz
sudo tar -xzf kafka_2.13-4.4.0.tgz -C /opt
sudo ln -sfn /opt/kafka_2.13-4.4.0 /opt/kafka
sudo useradd -r -m -d /opt/kafka kafka || true
sudo mkdir -p /data/kafka-logs && sudo chown -R kafka:kafka /data/kafka-logs
```

编辑 `/opt/kafka/config/server.properties`（KRaft 单节点，仅内网）：

```properties
process.roles=broker,controller
node.id=1
controller.quorum.voters=1@127.0.0.1:9093
listeners=PLAINTEXT://:9092,CONTROLLER://:9093
advertised.listeners=PLAINTEXT://<平台内网IP>:9092
controller.listener.names=CONTROLLER
listener.security.protocol.map=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT
log.dirs=/data/kafka-logs
num.partitions=3
auto.create.topics.enable=false
```

格式化并启动：

```bash
CLUSTER_ID=$(/opt/kafka/bin/kafka-storage.sh random-uuid)
sudo -u kafka /opt/kafka/bin/kafka-storage.sh format --standalone -t "$CLUSTER_ID" -c /opt/kafka/config/server.properties
sudo -u kafka /opt/kafka/bin/kafka-server-start.sh -daemon /opt/kafka/config/server.properties

# 预创建入站主题（本方案关闭了自动建主题）
/opt/kafka/bin/kafka-topics.sh --bootstrap-server 127.0.0.1:9092 \
  --create --if-not-exists --topic iot.edge.ingest --partitions 3 --replication-factor 1
/opt/kafka/bin/kafka-topics.sh --bootstrap-server 127.0.0.1:9092 --list
```

### 4.7 安装 Nginx

```bash
sudo apt-get install -y nginx
sudo systemctl enable --now nginx
```

---

## 5. 后端部署

### 5.1 获取与发布

```bash
# 构建机（需 .NET 8 SDK）
cd /path/to/IoTGateway.Server
dotnet restore
dotnet publish Admin.NET.Web.Entry/Admin.NET.Web.Entry.csproj -c Release -o /tmp/iotgateway-publish

# 目标机
sudo mkdir -p /opt/iotgateway
sudo rsync -a /tmp/iotgateway-publish/ /opt/iotgateway/
sudo useradd -r -s /usr/sbin/nologin iotgateway || true
sudo chown -R iotgateway:iotgateway /opt/iotgateway
```

发布目录应包含：

```
Admin.NET.Web.Entry.dll
Configuration/           数据库、JWT、缓存等框架配置
appsettings.json         IoT 业务配置（IotHistory / Kafka）
drivers/net8.0/*.dll     驱动插件（随发布复制）
wwwroot/                 上传/模板/静态资源
```

### 5.2 数据库配置

编辑 `Configuration/Database.json`（源码路径 `Admin.NET.Application/Configuration/Database.json`）：

```json
{
  "DbConnection": {
    "EnableConsoleSql": false,
    "ConnectionConfigs": [
      {
        "DbType": "PostgreSQL",
        "DbNickName": "系统库",
        "ConnectionString": "PORT=5432;DATABASE=iotgateway;HOST=127.0.0.1;PASSWORD=强密码;USER ID=iotgateway",
        "DbSettings": {
          "EnableInitDb": true,
          "EnableInitView": true,
          "EnableDiffLog": false,
          "EnableUnderLine": false,
          "EnableConnEncrypt": false
        },
        "TableSettings": { "EnableInitTable": true, "EnableIncreTable": false },
        "SeedSettings": { "EnableInitSeed": true, "EnableIncreSeed": false }
      }
    ]
  }
}
```

> 生产环境稳定后建议将 `EnableInitDb` / `EnableInitTable` / `EnableInitSeed` 改为 `false`，避免每次启动执行结构比对与种子写入；升级含新实体时再临时打开。

### 5.3 框架关键配置

| 文件 | 关键项 | 说明 |
|---|---|---|
| `Configuration/App.json` | `Urls`: `http://*:5005` | 后端监听端口；MQTT 1888 在 `Program.cs` 固定监听 |
| `Configuration/JWT.json` | `IssuerSigningKey` | **必须更换**为 32 字节以上随机密钥 |
| `Configuration/App.json` | `Cryptogram.PublicKey/PrivateKey` | 前端登录 SM2 加密公钥，换后同步改前端 `VITE_SM_PUBLIC_KEY` |
| `Configuration/App.json` | `SnowId.WorkerId` | 多实例部署时全局唯一 |
| `Configuration/Cache.json` | `CacheType`: `Memory`/`Redis` | 集群/多实例建议 Redis |
| `Configuration/App.json` | `InjectSpecificationDocument` | 是否在生产暴露 API 文档 |

### 5.4 IoT 业务配置（`appsettings.json`）

```json
{
  "IotHistory": {
    "Enabled": true,
    "ConnectionString": "Host=127.0.0.1;Port=5432;Database=iotgateway_ts;Username=iotgateway;Password=强密码",
    "BatchSize": 500,
    "FlushIntervalMs": 1000,
    "RetentionDays": 30
  },
  "Kafka": {
    "Enabled": false,
    "BootstrapServers": "127.0.0.1:9092",
    "ConsumerGroup": "iotgateway-ingest",
    "IngestTopic": "iot.edge.ingest",
    "ForwardEnabled": false,
    "ForwardTopic": "iot.telemetry"
  }
}
```

| 配置项 | 默认 | 说明 |
|---|---|---|
| `IotHistory.Enabled` | true | 关闭后不写入时序库（采集与告警仍工作） |
| `IotHistory.BatchSize` | 500 | 单批写入条数 |
| `IotHistory.FlushIntervalMs` | 1000 | 最长攒批时间 |
| `IotHistory.RetentionDays` | 30 | 超表保留天数，0 表示不清理 |
| `Kafka.Enabled` | false | Kafka 入站通道开关，false 不影响 MQTT |
| `Kafka.BootstrapServers` | 127.0.0.1:9092 | Broker 地址 |
| `Kafka.ConsumerGroup` | iotgateway-ingest | 消费组 |
| `Kafka.IngestTopic` | iot.edge.ingest | 入站主题 |
| `Kafka.ForwardEnabled` | false | 是否把 MQTT 入站消息转发到 Kafka |
| `Kafka.ForwardTopic` | iot.telemetry | 转发主题 |

> 也可用环境变量覆盖：`Kafka__Enabled=true`、`Kafka__ForwardEnabled=true`、`IotHistory__ConnectionString=...`。

### 5.5 首次启动与自动初始化

```bash
cd /opt/iotgateway
sudo -u iotgateway ASPNETCORE_ENVIRONMENT=Production \
  ASPNETCORE_URLS=http://0.0.0.0:5005 \
  dotnet Admin.NET.Web.Entry.dll
```

首次启动会依次完成：

1. 连接 PostgreSQL，创建/比对系统表与 IoT 业务表；
2. 写入种子数据：默认租户、超级管理员、角色/菜单、IoT 菜单与租户菜单绑定、传输配置；
3. 执行 `IotOrgSchemaMigration.Migrate()` 修正组织根节点；
4. `IotHistorySink` 创建 `iot_metric` 表、hypertable、索引与保留策略；
5. 启动内嵌 MQTT Broker（1888）、采集运行时、告警引擎、Kafka 入站（若启用）。

健康日志关键字：

```
数据库连接成功。
IoT MQTT Broker 已启动，TCP/WebSocket 端口 1888
IotHistory 启动，连接 <host>/iotgateway_ts，批量 500，间隔 1000ms，保留 30 天
Kafka 入站通道已启动：...（启用时）
```

### 5.6 systemd 托管

```ini
# /etc/systemd/system/iotgateway.service
[Unit]
Description=IoTGateway Data Acquisition Platform
After=network.target postgresql.service

[Service]
Type=simple
WorkingDirectory=/opt/iotgateway
ExecStart=/usr/bin/dotnet /opt/iotgateway/Admin.NET.Web.Entry.dll
User=iotgateway
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5005
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Restart=always
RestartSec=5
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now iotgateway
sudo systemctl status iotgateway
```

### 5.7 驱动插件部署

- 发布包已包含 `drivers/net8.0/*.dll`；新增驱动时把 DLL 及其依赖放入该目录。
- 在「驱动管理 → 重新加载」或重启后端后生效。
- 驱动类需实现 `IDriver` 并提供 `(string deviceName, ILogger logger)` 构造函数。
- 注意平台架构一致（x64/arm64），依赖 DLL 必须齐全。

---

## 6. 前端部署

### 6.1 环境变量

前端通过 `window.__env__`（构建时由 Vite 写入 `dist/config.js`）读取配置：

| 变量 | 说明 |
|---|---|
| `VITE_API_URL` | 接口基址；留空表示同源（由 Nginx 代理 `/api`） |
| `VITE_PUBLIC_PATH` | 部署子路径，如 `/iot/`；根路径留空 |
| `VITE_PORT` | 开发端口，默认 8888 |
| `VITE_SM_PUBLIC_KEY` | 与后端 `Cryptogram.PublicKey` 一致 |

生产环境 `.env.production`：

```ini
ENV = production
VITE_API_URL =
VITE_PUBLIC_PATH =
VITE_DEFAULT_USER =
VITE_DEFAULT_USER_PASSWORD =
```

### 6.2 构建

```bash
cd IoTGateway.Web
pnpm install --frozen-lockfile
pnpm build            # 产物在 dist/
sudo mkdir -p /var/www/iotgateway
sudo rsync -a --delete dist/ /var/www/iotgateway/
```

### 6.3 Nginx 配置

```nginx
server {
    listen 80;
    server_name iot.example.com;

    root /var/www/iotgateway;
    index index.html;

    # 压缩（Vite 同时生成 .gz/.br 预压缩文件，可用 gzip_static）
    gzip on;
    gzip_static on;
    gzip_comp_level 5;
    gzip_min_length 1024;
    gzip_types text/plain text/css application/javascript application/json image/svg+xml;

    # 前端路由（history 模式）
    location / {
        try_files $uri $uri/ /index.html;
    }

    # 后端 API
    location /api/ {
        proxy_pass http://127.0.0.1:5005;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
        client_max_body_size 200m;
    }

    # 上传与 SSE
    location /upload/ { proxy_pass http://127.0.0.1:5005; }
    location /sse/ {
        proxy_pass http://127.0.0.1:5005;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_buffering off;
    }

    # MQTT over WebSocket
    location /mqtt {
        proxy_pass http://127.0.0.1:5005;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
    }

    # 静态资源缓存
    location /assets/ {
        expires 30d;
        add_header Cache-Control "public, immutable";
    }
}
```

HTTPS 建议使用证书：

```bash
sudo certbot --nginx -d iot.example.com
```

> 若需访问接口文档，可将 `/sapi` 反向代理到 5005（生产建议限制来源 IP）。

---

## 7. 系统初始化与配置

### 7.1 登录

| 项 | 默认值 |
|---|---|
| 地址 | `http://<服务器>/` |
| 租户 | `1300000000001`（默认租户，登录页一般无需填写） |
| 超管账号 | `superAdmin.NET` |
| 超管密码 | `Admin.NET++010101` |

**首次登录后立即修改密码**，并删除/禁用演示账号（`Admin.NET`、`TestUser*`）。

### 7.2 基础配置顺序

1. **组织**：系统管理 → 机构管理，建立「集团 / 工厂 / 车间 / 产线」树。
2. **驱动**：IoT 网关 → 驱动管理，确认已加载驱动；选择驱动文件与程序集。
3. **设备**：设备管理，填写名称、所属组织、驱动、采集周期、自动启动等。
4. **通讯配置**：按驱动参数填写 IP/端口/站号等。
5. **变量**：为设备添加变量，配置地址、数据类型、字节序、读写权限、表达式。
6. **边缘节点**：边缘节点页新增节点，填写节点编号（唯一）与所属组织；系统自动生成接入密钥。
7. **告警**：配置通知渠道 → 告警规则（阈值/状态/变化率、组合方式、连续样本、通知渠道）。
8. **报表**：报表 BI 页选择设备/变量/时间范围/聚合/粒度查询并导出 CSV。

### 7.3 菜单与权限

- 菜单由后端 `sysmenu` 驱动，页面可见性还需 `systenantmenu`（租户→菜单）绑定。
- IoT 菜单已通过种子数据初始化；报表菜单在存量库升级时需执行 [附录 C](#附录-c报表菜单补充-sql)。
- 按钮权限：`iotDevice:add`、`iotEdge:update` 等，按角色分配；超管直通。

### 7.4 边缘节点密钥

- 新增节点自动生成 32 位十六进制密钥，接口不返回该字段。
- 密钥泄露或更换时，在边缘节点页执行「重置密钥」，返回明文仅一次，需同步到边缘端配置。
- 心跳/遥测未携带或不匹配密钥时被拒绝，节点不会上线。

---

## 8. 边缘节点接入协议

### 8.1 接入流程

```
1. 中心创建边缘节点 → 获取 nodeCode 与 accessKey
2. 边缘端配置：中心地址、nodeCode、accessKey、通道（MQTT/Kafka）
3. 周期发送 heartbeat（建议 30s）→ 中心置 online=true
4. 上报 telemetry（单条或数组）→ 中心落库 + 告警计算
5. 中心可下发 config（retain）与 command（非 retain）
6. 超过 90s 无心跳/上报 → 离线检测作业置 online=false
```

### 8.2 MQTT 主题

| 方向 | 主题 | retain | 说明 |
|---|---|---|---|
| 上行 | `edge/{nodeCode}/heartbeat` | 否 | 心跳，payload 可含版本 |
| 上行 | `edge/{nodeCode}/telemetry` | 否 | 遥测，单条对象或对象数组 |
| 下行 | `edge/{nodeCode}/config` | 是 | 配置下发，边缘端订阅后即得当前配置 |
| 下行 | `edge/{nodeCode}/command` | 否 | 实时指令 |

Broker：`<平台IP>:1888`（TCP）；WebSocket：`ws://<平台IP>:5005/mqtt`。
当前 Broker 不校验客户端用户名密码（连接均放行），业务鉴权依赖载荷中的 `accessKey`。

### 8.3 心跳载荷

```json
{
  "accessKey": "f1b413ee287543afa0efae1e2161843b",
  "version": "1.2.0"
}
```

也可走 HTTP（匿名接口，仍校验密钥）：

```bash
curl -X POST http://<平台IP>:5005/api/iotEdgeNode/heartbeat \
  -H 'Content-Type: application/json' \
  -d '{"nodeCode":"PLANT-EDGE-01","accessKey":"<KEY>","version":"1.2.0"}'
```

### 8.4 遥测载荷

单条：

```json
{
  "accessKey": "<KEY>",
  "deviceId": 7368176410392,
  "variableId": 2704104529996,
  "value": 321.123,
  "ts": "2026-11-23T23:21:00+08:00",
  "status": 0
}
```

数组：

```json
[
  { "accessKey": "<KEY>", "deviceId": 7368176410392, "variableId": 2704104529996, "value": 321.123 },
  { "accessKey": "<KEY>", "deviceName": "Modbus", "variableName": "random4", "value": 12.5 }
]
```

字段说明：

| 字段 | 必填 | 说明 |
|---|---|---|
| `accessKey` | 是 | 节点密钥；数组中可取任一元素的值 |
| `deviceId` / `deviceName` | 二选一 | 设备 ID 或设备名称（名称需唯一） |
| `variableId` / `variableName` | 二选一 | 变量 ID 或变量名称（名称在设备内唯一） |
| `value` | 是 | 数值/字符串/布尔；字符串非数字时写入 `value_text` |
| `ts` | 否 | ISO 8601；缺省为服务器当前时间；无时区按本地时间解释 |
| `status` | 否 | 变量状态，0=Good；非 0 表示异常质量 |

处理语义：

- 遥测成功即把节点置为在线并更新最后上报时间；
- 同时入历史队列与告警队列（历史按 UTC 存储，展示按本地时区）；
- 无效测点（设备/变量解析失败）会被跳过并记录日志。

### 8.5 下行配置与指令

接口：`POST /api/iotEdgeNode/send`（需登录令牌）

```json
{
  "id": 873590305247301,
  "kind": "config",
  "payload": "{\"pollInterval\":5000,\"variables\":[...]}"
}
```

- `kind=config` → 发布到 `edge/{nodeCode}/config`，retain=true；
- `kind=command` → 发布到 `edge/{nodeCode}/command`，retain=false；
- 返回 `{ topic, retain, online }`。

### 8.6 Kafka 接入

启用：

```bash
Kafka__Enabled=true
# 可选：把 MQTT 入站消息同时转发到 Kafka
Kafka__ForwardEnabled=true
```

消息体（主题 `iot.edge.ingest`）：

```json
{
  "nodeCode": "PLANT-EDGE-01",
  "type": "telemetry",
  "payload": {
    "accessKey": "<KEY>",
    "deviceId": 7368176410392,
    "variableId": 2704104529996,
    "value": 321.123
  }
}
```

- `type`：`telemetry` 或 `heartbeat`；
- `payload` 可为对象，也可为字符串（字符串内容与 MQTT 载荷一致）；
- 节点编号、密钥校验与 MQTT 通道完全一致；
- 消费组 `iotgateway-ingest`，`AutoOffsetReset=Earliest`，自动提交位点；
- 出向转发主题 `iot.telemetry`，消息体 `{ nodeCode, type, payload, ts }`；
- 入站与转发主题分离，避免回环重复；
- 当前为至少一次语义，重复消息未做去重，边缘端应按 `ts` 幂等处理。

验证命令：

```bash
# 生产消息
printf '%s\n' '{"nodeCode":"PLANT-EDGE-01","type":"telemetry","payload":{"accessKey":"<KEY>","deviceId":7368176410392,"variableId":2704104529996,"value":321.123}}' \
  | /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server 127.0.0.1:9092 --topic iot.edge.ingest

# 查看转发（需 ForwardEnabled=true）
/opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server 127.0.0.1:9092 --topic iot.telemetry --from-beginning
```

### 8.7 MQTT 调试示例

```bash
# 心跳
mosquitto_pub -h <平台IP> -p 1888 -t edge/PLANT-EDGE-01/heartbeat \
  -m '{"accessKey":"<KEY>","version":"1.0.0"}'

# 遥测
mosquitto_pub -h <平台IP> -p 1888 -t edge/PLANT-EDGE-01/telemetry \
  -m '{"accessKey":"<KEY>","deviceId":7368176410392,"variableId":2704104529996,"value":77.77}'

# 下行（需订阅验证）
mosquitto_sub -h <平台IP> -p 1888 -t 'edge/PLANT-EDGE-01/config' -v
```

---

## 9. 时序存储设计

### 9.1 表结构

数据库：`iotgateway_ts`（TimescaleDB）

```sql
CREATE TABLE iot_metric (
  time        timestamptz NOT NULL,
  device_id   bigint      NOT NULL,
  variable_id bigint      NOT NULL,
  value       double precision,
  value_text  text,
  status      integer     NOT NULL DEFAULT 0
);

SELECT create_hypertable('iot_metric', 'time',
  if_not_exists => TRUE,
  chunk_time_interval => INTERVAL '1 day');

CREATE INDEX IF NOT EXISTS idx_iot_metric_dv_time
  ON iot_metric (device_id, variable_id, time DESC);

SELECT add_retention_policy('iot_metric', make_interval(days => 30));
```

- 按天分块；数值型写 `value`，非数值写 `value_text`；
- 保留策略由 `IotHistory.RetentionDays` 控制，启动时重建；
- 写入使用二进制 `COPY` 批量导入，失败重试 3 次；
- 队列容量为 `max(BatchSize×8, 4096)`，溢出丢弃最旧数据并记录告警日志。

### 9.2 查询与聚合

历史接口 `/api/iotHistory/queryHistory`：

```json
{
  "deviceId": 7368176410392,
  "variableIds": [2704104529996],
  "startTime": "2026-11-23T00:00:00+08:00",
  "endTime": "2026-11-24T00:00:00+08:00",
  "aggregate": "avg",
  "bucketSeconds": 3600,
  "limit": 10000
}
```

- `aggregate`：`raw` / `avg` / `max` / `min` / `sum` / `count`；
- 聚合使用 `time_bucket(make_interval(secs => bucketSeconds), time)`；
- 时间入参支持带时区 ISO 8601，统一转换为 UTC 区间查询；
- 原始查询默认上限 10000 条（报表原始模式上限 20000 条）。

### 9.3 报表查询

接口 `/api/iotReport/query`：

```json
{
  "deviceId": 7368176410392,
  "variableIds": [2704104529996],
  "startTime": "2026-11-01T00:00:00+08:00",
  "endTime": "2026-11-24T00:00:00+08:00",
  "aggregate": "avg",
  "granularity": "hour"
}
```

| 参数 | 取值 | 说明 |
|---|---|---|
| `aggregate` | raw/avg/max/min/sum/count | 聚合方式 |
| `granularity` | raw/minute/hour/day/month | 分钟=60s、小时=3600s、天=86400s、月=2592000s（30 天） |

返回透视后的列定义与行数据；前端以「时间 + 各变量」动态列表格展示，并可导出 UTF-8 BOM CSV。

---

## 10. API 清单

> 除特别说明外均需登录令牌（`Authorization: Bearer <token>`），并按组织权限过滤。
> 接口文档：`http://<平台IP>:5005/sapi`；MCP 端点：`/mcp`（需授权）。

### 10.1 设备与变量

| 模块 | 路由 | 方法 | 说明 |
|---|---|---|---|
| 设备 | `/api/iotDevice/page` | POST | 分页 |
| 设备 | `/api/iotDevice/list` | GET | 树/列表 |
| 设备 | `/api/iotDevice/detail` | GET | 详情 |
| 设备 | `/api/iotDevice/add` | POST | 新增 |
| 设备 | `/api/iotDevice/update` | POST | 修改 |
| 设备 | `/api/iotDevice/delete` | POST | 删除 |
| 设备 | `/api/iotDevice/batchDelete` | POST | 批量删除 |
| 设备 | `/api/iotDevice/batchUpdate` | POST | 批量修改 |
| 设备 | `/api/iotDevice/requestAttributes` | POST | 请求驱动属性 |
| 设备 | `/api/iotDevice/export` / `import` | GET/POST | 备份导出/导入 |
| 设备 | `/api/iotDevice/stats` | GET | 仪表盘统计 |
| 变量 | `/api/iotDeviceVariable/page` | POST | 分页 |
| 变量 | `/api/iotDeviceVariable/list` | GET | 按设备列表 |
| 变量 | `/api/iotDeviceVariable/write` | POST | 批量写值 |
| 变量 | `/api/iotDeviceVariable/add` / `update` / `delete` / `batchDelete` | POST | 维护 |
| 变量 | `/api/iotDeviceVariable/export` / `import` | GET/POST | 导入导出 |
| 通讯配置 | `/api/iotDeviceConfig/page` / `list` | POST/GET | 查询 |
| 通讯配置 | `/api/iotDeviceConfig/add` / `update` / `delete` / `batchDelete` / `batchUpdate` | POST | 维护 |
| 通讯配置 | `/api/iotDeviceConfig/export` | GET | 导出 |

### 10.2 驱动与传输

| 模块 | 路由 | 方法 | 说明 |
|---|---|---|---|
| 驱动 | `/api/iotDriver/page` / `list` | POST/GET | 查询 |
| 驱动 | `/api/iotDriver/driverFiles` | GET | 扫描可用驱动文件 |
| 驱动 | `/api/iotDriver/driverConfigs` | GET | 驱动通讯参数定义 |
| 驱动 | `/api/iotDriver/driverMethods` | GET | 驱动方法定义 |
| 驱动 | `/api/iotDriver/reload` | POST | 重新加载驱动（超管） |
| 驱动 | `/api/iotDriver/add` / `update` / `delete` / `batchDelete` / `batchUpdate` | POST | 维护 |
| 传输配置 | `/api/iotSystemConfig/get` / `save` | GET/POST | 网关/MQTT/上游平台配置 |
| MQTT | `/api/iotMqtt/status` | GET | Broker 运行状态与客户端列表 |

### 10.3 边缘节点

| 路由 | 方法 | 鉴权 | 说明 |
|---|---|---|---|
| `/api/iotEdgeNode/page` | POST | 登录 | 分页 |
| `/api/iotEdgeNode/list` | POST | 登录 | 列表 |
| `/api/iotEdgeNode/detail` | GET | 登录 | 详情 |
| `/api/iotEdgeNode/heartbeat` | POST | **匿名 + 密钥** | HTTP 心跳 |
| `/api/iotEdgeNode/regenerateKey` | POST | 登录 | 重置接入密钥（明文仅返回一次） |
| `/api/iotEdgeNode/send` | POST | 登录 | 下发 config/command |
| `/api/iotEdgeNode/add` / `update` / `delete` / `batchDelete` | POST | 登录 | 维护 |

### 10.4 历史、报表、告警

| 路由 | 方法 | 说明 |
|---|---|---|
| `/api/iotHistory/queryHistory` | POST | 历史数据查询（raw/聚合） |
| `/api/iotReport/query` | POST | 报表透视查询 |
| `/api/iotAlarmRule/page` / `detail` / `add` / `update` / `delete` | POST/GET | 告警规则 |
| `/api/iotAlarmEvent/page` / `list` / `detail` / `ack` / `close` / `overview` | POST/GET | 告警事件、确认、闭环、统计 |
| `/api/iotAlarmChannel/page` / `options` / `add` / `update` / `delete` / `test` | POST/GET | 通知渠道（邮件/钉钉/飞书） |
| `/api/iotAlarmChannel/notificationPage` | POST | 通知日志 |
| `/api/iotRpcLog/page` / `detail` / `export` / `delete` / `batchDelete` / `clear` | POST/GET | RPC 日志 |
| `/api/iotDashboard/*` | GET | 首页图表统计 |

---

## 11. 运维手册

### 11.1 启停与健康检查

```bash
sudo systemctl start|stop|restart|status iotgateway

# 端口
ss -ltnp | grep -E ':(5005|1888|5432|9092)\b'

# 接口存活（未登录时返回 401 也说明服务在监听）
curl -I http://127.0.0.1:5005/sapi

# 数据库连通
PGPASSWORD='<pwd>' psql -h 127.0.0.1 -U iotgateway -d iotgateway -c 'select 1;'
PGPASSWORD='<pwd>' psql -h 127.0.0.1 -U iotgateway -d iotgateway_ts -c 'select count(*) from iot_metric;'
```

### 11.2 日志

- 应用日志：`/opt/iotgateway/logs/`（Admin.NET 按天切分）。
- 关键关键字：

| 关键字 | 含义 |
|---|---|
| `数据库连接成功` | 配置库连通 |
| `驱动加载完成，共 N 个` | 驱动扫描结果 |
| `IoT MQTT Broker 已启动` | 1888 就绪 |
| `IotHistory 启动` / `初始化 TimescaleDB 失败` | 时序写入状态 |
| `Kafka 入站通道已启动` / `未启用` | Kafka 状态 |
| `接入密钥错误，拒绝` | 边缘鉴权失败 |
| `队列溢出丢弃` | 写库吞吐不足 |
| `边缘节点离线检测：N 个节点…` | 节点心跳超时 |

### 11.3 备份与恢复

```bash
# 备份（建议每日）
pg_dump -Fc -h 127.0.0.1 -U iotgateway -d iotgateway    -f /backup/iotgateway_$(date +%F).dump
pg_dump -Fc -h 127.0.0.1 -U iotgateway -d iotgateway_ts -f /backup/iotgateway_ts_$(date +%F).dump
tar czf /backup/iotgateway_conf_$(date +%F).tar.gz -C /opt/iotgateway Configuration appsettings.json drivers

# 恢复
pg_restore -h 127.0.0.1 -U iotgateway -d iotgateway    --clean --if-exists /backup/iotgateway_xxx.dump
pg_restore -h 127.0.0.1 -U iotgateway -d iotgateway_ts --clean --if-exists /backup/iotgateway_ts_xxx.dump
```

> 时序库恢复要求目标库已安装 timescaledb 扩展；恢复后如遇超表元数据问题，按 TimescaleDB 官方流程执行 `timescaledb_pre_restore()` / `timescaledb_post_restore()`。

### 11.4 监控建议

| 指标 | 采集方式 | 告警阈值建议 |
|---|---|---|
| 后端存活 | HTTP `/sapi` 或 systemd | 连续 3 次失败 |
| 数据库连接数 | `pg_stat_activity` | > 80% max_connections |
| 时序表增长 | `pg_total_relation_size('iot_metric')` | 按磁盘余量 |
| 写入丢弃 | 日志 `队列溢出丢弃` | 出现即告警 |
| 节点离线数 | `iotedgenode` 中 `online=false` | 超阈值 |
| Kafka 消费延迟 | `kafka-consumer-groups.sh --describe` | lag 持续增长 |
| 磁盘/CPU/内存 | node_exporter 等 | 常规阈值 |

### 11.5 常见故障排查

| 现象 | 可能原因 | 处理 |
|---|---|---|
| 启动失败 `Connection open error` | PG 未就绪/账号密码错误/库不存在 | 检查 PG 服务与连接串；偶发可等待后重试 |
| 5005/1888 端口占用 | 旧进程未退出 | `ss -ltnp` 查 PID，停止旧进程 |
| `iot_metric` 无数据 | `IotHistory.Enabled=false`、连接串错误、扩展缺失 | 看日志 `IotHistory ... 失败`；确认 `SHOW shared_preload_libraries` |
| 历史查询为空 | 时间范围无数据/时区理解错误/设备变量不匹配 | 用 SQL 直接查最近记录；入参带时区 |
| 报表接口 404 | 版本未包含 `IotReportService` 或路由未注册 | 使用 `/api/iotReport/query`；确认发布包版本 |
| 报表菜单不显示 | 缺 `systenantmenu` 绑定 | 执行附录 C 的 SQL，整页刷新 |
| 边缘节点一直离线 | 心跳未发/密钥错误/主题格式错误/节点编号不一致 | 查日志 `接入密钥错误`；核对 `edge/{nodeCode}/...` |
| Kafka 日志 `Unknown topic` | 主题未创建 | 执行第 4.6 节建主题命令 |
| Kafka 消息不落库 | `Kafka.Enabled=false` / 消费组位点 / payload 缺 `nodeCode` | 看日志；重置消费组位点后重试 |
| 驱动加载 0 个 | `drivers/net8.0` 缺失、依赖不全、架构不符 | 补全 DLL；点击「重新加载」 |
| 页面刷新 404 | Nginx 未配置 SPA 回退 | 增加 `try_files $uri $uri/ /index.html` |
| WebSocket 连接失败 | Nginx 未转发 Upgrade | 增加 `/mqtt` 的 `Upgrade`/`Connection` 头 |
| CSV 中文乱码 | Excel 未识别 UTF-8 | 文件已带 BOM；仍乱码可用「数据→自文本」导入 |

### 11.6 升级与回滚

```bash
# 升级
sudo systemctl stop iotgateway
sudo cp -a /opt/iotgateway /opt/iotgateway.bak.$(date +%F)
sudo rsync -a /tmp/iotgateway-publish/ /opt/iotgateway/
# 如含新实体，临时开启 CodeFirst 初始化后启动
sudo systemctl start iotgateway

# 回滚
sudo systemctl stop iotgateway
sudo rsync -a --delete /opt/iotgateway.bak.<日期>/ /opt/iotgateway/
sudo systemctl start iotgateway
```

> 数据回滚需配合数据库备份；仅回滚程序不自动回滚表结构。

---

## 12. 安全加固

1. **账号与密码**
   - 首次登录修改超管密码；删除演示账号。
   - 生产开启密码强度策略（`App.json → Cryptogram.StrongPassword`）。
   - 更换 `JWT.json` 的签发密钥与 `Cryptogram` SM2 密钥对，并同步前端 `VITE_SM_PUBLIC_KEY`。

2. **边缘接入**
   - 每个节点独立 32 位密钥；定期轮换（重置密钥后同步边缘端）。
   - 心跳/遥测必须携带密钥，错误请求直接拒绝。
   - 1888 端口仅对边缘网段开放；禁止公网直连。

3. **MQTT Broker 限制**
   - 当前 Broker 对连接不做用户名/密码校验，也不启用 TLS。
   - 若必须跨不可信网络，建议：置于 VPN/专线内；或前置 TLS MQTT 代理；或在后续版本中为 Broker 增加账号与 TLS 支持。

4. **Kafka**
   - 当前为 PLAINTEXT；仅内网使用，按需配置 ACL/SASL。
   - 入站主题与转发主题分离，避免回环。

5. **数据库**
   - 业务账号最小权限；不对公网开放 5432。
   - 定期备份并校验恢复流程。
   - 生产稳定后关闭 CodeFirst 自动初始化开关。

6. **Web 层**
   - 全站 HTTPS；限制 `/sapi`、`/mcp` 的来源 IP。
   - Nginx 增加安全响应头（HSTS、X-Content-Type-Options 等）。
   - 上传目录禁止脚本执行。

7. **供应链**
   - 驱动插件与 NuGet 包来自可信来源；商业驱动确认授权。
   - 发布包做哈希校验并留存版本清单。

---

## 13. 验收清单

### 13.1 部署验收

- [ ] PostgreSQL 两库可连接，`timescaledb` 扩展已加载。
- [ ] `ss` 可见 5005、1888（启用 Kafka 时含 9092）。
- [ ] 后端日志出现「数据库连接成功」「MQTT Broker 已启动」「IotHistory 启动」。
- [ ] Nginx 可打开登录页，静态资源与 `/api` 代理正常。
- [ ] 使用超管登录成功，IoT 菜单完整显示。
- [ ] 驱动管理可列出驱动文件，设备/变量可新增。

### 13.2 采集链路验收（MQTT）

- [ ] 新增边缘节点，取得 `nodeCode` 与密钥。
- [ ] 发送心跳后节点 `online=true`，`lastheartbeattime` 更新。
- [ ] 发送遥测 `value=321.123` 后，`iot_metric` 出现对应记录。
- [ ] 错误密钥遥测被拒绝（不落库，日志有「接入密钥错误」）。
- [ ] 90s 无心跳后节点自动离线。

### 13.3 采集链路验收（Kafka，可选）

- [ ] `Kafka__Enabled=true` 启动后日志出现「Kafka 入站通道已启动」。
- [ ] 生产心跳/遥测到 `iot.edge.ingest`，数据落库且节点在线。
- [ ] 错误密钥消息被拒绝。
- [ ] `ForwardEnabled=true` 时，MQTT 遥测可在 `iot.telemetry` 消费到（消息体含 nodeCode/type/payload/ts）。

### 13.4 业务功能验收

- [ ] 历史趋势页可选设备/变量/时间范围绘图。
- [ ] 报表 BI 可出表格与图表，CSV 导出中文正常。
- [ ] 告警规则触发后生成事件，确认/闭环状态正确。
- [ ] 通知渠道「测试」可收到邮件/钉钉/飞书消息。
- [ ] 不同组织用户只能看到授权范围内的设备与数据。

---

## 14. 附录

### 附录 A：关键配置文件汇总

| 文件 | 作用 |
|---|---|
| `Configuration/Database.json` | 数据库类型、连接串、CodeFirst 与种子开关 |
| `Configuration/App.json` | 端口、SM2 密钥、雪花 Id、API 文档开关、跨域 |
| `Configuration/JWT.json` | JWT 签发密钥与有效期 |
| `Configuration/Cache.json` | 内存/Redis 缓存、集群 |
| `Configuration/EventBus.json` | 事件总线与 Redis 队列 |
| `appsettings.json` | `IotHistory` 时序存储、`Kafka` 双通道 |
| `appsettings.Development.json` | 开发环境覆盖 |

### 附录 B：数据库对象

**配置库 `iotgateway`（IoT 业务表）**

| 表 | 说明 |
|---|---|
| `iotdevice` | 设备（含组织、边缘节点归属） |
| `iotdeviceconfig` | 设备通讯配置 |
| `iotdevicevariable` | 变量（地址、类型、字节序、表达式） |
| `iotdriver` | 驱动注册信息 |
| `iotsystemconfig` | 传输配置（网关/MQTT/上游平台） |
| `iotedgenode` | 边缘节点（编号、密钥、在线状态、心跳时间） |
| `iotalarmrule` / `iotalarmcondition` | 告警规则与条件 |
| `iotalarmevent` | 告警事件（活动/确认/恢复/闭环） |
| `iotalarmchannel` | 通知渠道（邮件/钉钉/飞书） |
| `iotalarmnotificationlog` | 通知发送日志 |
| `iotrpclog` | RPC 调用日志 |

**系统表**：`sysuser`、`sysrole`、`sysmenu`、`sysorg`、`systenant`、`systenantmenu`、`sysrolemenu`、`sysjob*`、`syslog*` 等（Admin.NET 标准表）。

**时序库 `iotgateway_ts`**：`iot_metric`（hypertable）。

### 附录 C：报表菜单补充 SQL

存量库升级或全新库若未见「报表 BI」菜单，执行：

```sql
-- 菜单（sysmenu）
INSERT INTO sysmenu (id, pid, type, title, name, path, component, icon, isiframe, ishide, iskeepalive, isaffix, orderno, status, createtime)
VALUES (1311000011101, 1311000000101, 2, '报表BI', 'iotReport', '/iot/report', '/iot/report/index', 'ele-Document', false, false, true, false, 155, 1, now())
ON CONFLICT (id) DO NOTHING;

-- 租户绑定（systenantmenu，默认租户 1300000000001）
INSERT INTO systenantmenu (id, tenantid, menuid)
VALUES (1309000011101, 1300000000001, 1311000011101)
ON CONFLICT (id) DO NOTHING;
```

> `isiframe/ishide/iskeepalive/isaffix` 为 NOT NULL 字段，必须显式赋值，否则插入报 `null value in column "isiframe"`。
> Windows 本地部署可直接运行 `deploy\windows\05-init-report-menu.ps1`（脚本会在后端首次启动建表后执行同一份 SQL，可重复执行）。

> 若菜单仍不显示，确认当前登录租户为 `1300000000001`，并整页刷新（非路由热切换）。

### 附录 D：Kafka 常用命令

```bash
# 主题
/opt/kafka/bin/kafka-topics.sh --bootstrap-server 127.0.0.1:9092 --list
/opt/kafka/bin/kafka-topics.sh --bootstrap-server 127.0.0.1:9092 \
  --create --if-not-exists --topic iot.edge.ingest --partitions 3 --replication-factor 1
/opt/kafka/bin/kafka-topics.sh --bootstrap-server 127.0.0.1:9092 --describe --topic iot.edge.ingest

# 消费（调试）
/opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server 127.0.0.1:9092 \
  --topic iot.telemetry --from-beginning

# 消费组延迟
/opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server 127.0.0.1:9092 \
  --describe --group iotgateway-ingest

# 重置位点（谨慎）
/opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server 127.0.0.1:9092 \
  --group iotgateway-ingest --topic iot.edge.ingest --reset-offsets --to-earliest --execute
```

### 附录 E：实现与阶段对照

| 模块 | 关键实现 |
|---|---|
| 时序写入 | `IotHistorySink`（有界队列 + 批量 COPY + 重试 + 保留策略） |
| 时序查询 | `IotHistoryQuery`（raw / `time_bucket` 聚合，UTC 区间） |
| 报表 BI | `IotReportService`（变量 × 聚合 × 粒度透视） |
| 边缘 MQTT 入站 | `IotEdgeIngestService` → `IotEdgeIngestProcessor` |
| 边缘 Kafka 入站 | `IotKafkaIngestService` → `IotEdgeIngestProcessor` |
| Kafka 转发 | `IotKafkaProducer`（懒加载 Producer，异步发送） |
| 心跳鉴权 | `AccessKey` + 定长比较；新增自动生成、可重置 |
| 在线检测 | `IotEdgeNodeOnlineJob`（30s 扫描，90s 阈值） |
| 告警引擎 | `IotAlarmEngine`（规则计算 + 去抖 + 通知队列） |
| 下行通道 | `IotEdgeDownlink`（config retain=true / command retain=false） |

---

**文档结束**
