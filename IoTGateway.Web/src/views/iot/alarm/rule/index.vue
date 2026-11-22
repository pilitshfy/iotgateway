<template>
	<div class="iot-page">
		<el-card shadow="never">
			<el-form :inline="true" class="iot-search" @submit.prevent>
				<el-form-item label="所属组织">
					<el-tree-select
						v-model="query.orgId"
						:data="orgTree"
						:props="orgTreeProps"
						node-key="id"
						check-strictly
						default-expand-all
						filterable
						clearable
						placeholder="全部组织（含下级）"
						style="width: 210px"
					/>
				</el-form-item>
				<el-form-item label="设备">
					<el-select v-model="query.deviceId" placeholder="全部设备" filterable clearable style="width: 190px">
						<el-option v-for="item in deviceOptions" :key="item.id" :label="item.deviceName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="规则名称">
					<el-input v-model="query.keyword" placeholder="请输入规则名称" clearable style="width: 180px" @keyup.enter="search" />
				</el-form-item>
				<el-form-item label="级别">
					<el-select v-model="query.severity" placeholder="全部" clearable style="width: 110px">
						<el-option v-for="item in severityOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="状态">
					<el-select v-model="query.enabled" placeholder="全部" clearable style="width: 100px">
						<el-option label="启用" :value="true" />
						<el-option label="停用" :value="false" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="search">查询</el-button>
					<el-button @click="resetQuery">重置</el-button>
					<el-button type="success" @click="openAdd">新增规则</el-button>
					<el-button v-if="isSuperAdmin" @click="openChannelManager">渠道管理</el-button>
					<el-button @click="openNotifications">通知日志</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe>
				<el-table-column prop="name" label="规则名称" min-width="150" show-overflow-tooltip />
				<el-table-column prop="orgName" label="所属组织" min-width="130" show-overflow-tooltip />
				<el-table-column prop="deviceName" label="设备" min-width="140" show-overflow-tooltip />
				<el-table-column label="级别" width="90">
					<template #default="{ row }"><el-tag :type="severityTag(row.severity)">{{ severityLabel(row.severity) }}</el-tag></template>
				</el-table-column>
				<el-table-column label="条件" min-width="260">
					<template #default="{ row }">
						<div class="condition-summary">
							<el-tag size="small" effect="plain">{{ combineLabel(row.combineMode) }}</el-tag>
							<span v-for="condition in row.conditions || []" :key="condition.id">{{ conditionText(condition) }}</span>
						</div>
					</template>
				</el-table-column>
				<el-table-column label="触发/恢复" width="110" align="center">
					<template #default="{ row }">{{ row.triggerSamples }} / {{ row.recoverySamples }}</template>
				</el-table-column>
				<el-table-column label="重复通知" width="110" align="center">
					<template #default="{ row }">{{ row.suppressSeconds > 0 ? `${row.suppressSeconds} 秒` : '不重复' }}</template>
				</el-table-column>
				<el-table-column label="通知渠道" min-width="150" show-overflow-tooltip>
					<template #default="{ row }">{{ channelNames(row.channelIds) }}</template>
				</el-table-column>
				<el-table-column label="状态" width="80" align="center">
					<template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag></template>
				</el-table-column>
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="removeRule(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>
			<el-pagination
				v-model:current-page="query.page"
				v-model:page-size="query.pageSize"
				class="iot-pager"
				:total="total"
				:page-sizes="[10, 20, 50, 100]"
				layout="total, sizes, prev, pager, next, jumper"
				@current-change="loadData"
				@size-change="onPageSizeChange"
			/>
		</el-card>

		<el-drawer v-model="ruleVisible" :title="form.id ? '编辑告警规则' : '新增告警规则'" size="760px" destroy-on-close>
			<el-form :model="form" label-width="120px">
				<el-row :gutter="16">
					<el-col :span="12">
						<el-form-item label="规则名称" required><el-input v-model="form.name" maxlength="128" placeholder="请输入规则名称" /></el-form-item>
					</el-col>
					<el-col :span="12">
						<el-form-item label="采集设备" required>
							<el-select v-model="form.deviceId" filterable placeholder="请选择设备" style="width: 100%" @change="onFormDeviceChange">
								<el-option v-for="item in deviceOptions" :key="item.id" :label="item.deviceName" :value="item.id" />
							</el-select>
						</el-form-item>
					</el-col>
					<el-col :span="12">
						<el-form-item label="告警级别">
							<el-select v-model="form.severity" style="width: 100%">
								<el-option v-for="item in severityOptions" :key="item.value" :label="item.label" :value="item.value" />
							</el-select>
						</el-form-item>
					</el-col>
					<el-col :span="12">
						<el-form-item label="条件组合">
							<el-radio-group v-model="form.combineMode">
								<el-radio-button :value="0">AND 全部满足</el-radio-button>
								<el-radio-button :value="1">OR 任一满足</el-radio-button>
							</el-radio-group>
						</el-form-item>
					</el-col>
					<el-col :span="8">
						<el-form-item label="连续触发"><el-input-number v-model="form.triggerSamples" :min="1" :max="10000" style="width: 100%" /></el-form-item>
					</el-col>
					<el-col :span="8">
						<el-form-item label="连续恢复"><el-input-number v-model="form.recoverySamples" :min="1" :max="10000" style="width: 100%" /></el-form-item>
					</el-col>
					<el-col :span="8">
						<el-form-item label="是否启用"><el-switch v-model="form.enabled" /></el-form-item>
					</el-col>
					<el-col :span="24">
						<el-form-item label="重复通知间隔">
							<el-input-number v-model="form.suppressSeconds" :min="0" :max="31536000" style="width: 220px" />
							<span class="form-hint">秒，0 表示活动告警不重复通知</span>
						</el-form-item>
					</el-col>
					<el-col :span="24">
						<el-form-item label="通知渠道">
							<el-select v-model="form.channelIds" multiple collapse-tags collapse-tags-tooltip placeholder="选择通知渠道（可多选）" style="width: 100%">
								<el-option v-for="item in channels" :key="item.id" :label="`${item.name}（${channelTypeLabel(item.type)}${item.enabled ? '' : '，已停用'}）`" :value="item.id" />
							</el-select>
						</el-form-item>
					</el-col>
					<el-col :span="24"><el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" maxlength="512" /></el-form-item></el-col>
				</el-row>

				<el-divider content-position="left">告警条件</el-divider>
				<el-alert title="变化率单位为单位/分钟；同一规则的所有变量均来自所选设备。" type="info" :closable="false" class="condition-alert" />
				<div v-for="(condition, index) in form.conditions" :key="condition.key" class="condition-row">
					<div class="condition-index">{{ index + 1 }}</div>
					<el-select v-model="condition.variableId" filterable placeholder="选择变量" class="condition-variable">
						<el-option v-for="item in variables" :key="item.id" :label="item.alias ? `${item.name}（${item.alias}）` : item.name" :value="item.id" />
					</el-select>
					<el-select v-model="condition.operator" class="condition-operator" @change="onOperatorChange(condition)">
						<el-option v-for="item in operatorOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
					<el-select v-if="isStatusOperator(condition.operator)" v-model="condition.status" placeholder="变量状态" class="condition-value">
						<el-option v-for="item in variableStatusOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
					<template v-else>
						<el-input-number v-model="condition.value" :controls="false" placeholder="阈值/下限" class="condition-value" />
						<el-input-number v-if="isRangeOperator(condition.operator)" v-model="condition.secondValue" :controls="false" placeholder="上限" class="condition-second" />
					</template>
					<el-button link type="danger" :disabled="form.conditions.length === 1" @click="removeCondition(index)">删除</el-button>
				</div>
				<el-button type="primary" plain :disabled="!form.deviceId" @click="addCondition">添加条件</el-button>
			</el-form>
			<template #footer>
				<el-button @click="ruleVisible = false">取消</el-button>
				<el-button type="primary" :loading="saving" @click="saveRule">保存</el-button>
			</template>
		</el-drawer>

		<el-drawer v-model="channelVisible" title="通知渠道管理" size="92%" destroy-on-close>
			<div class="drawer-toolbar">
				<el-input v-model="channelQuery.keyword" placeholder="渠道名称" clearable style="width: 220px" @keyup.enter="searchChannels" />
				<el-button @click="searchChannels">查询</el-button>
				<el-button type="primary" @click="openChannelAdd">新增渠道</el-button>
			</div>
			<el-table v-loading="channelLoading" :data="channelRows" border stripe>
				<el-table-column prop="name" label="渠道名称" min-width="140" />
				<el-table-column label="类型" width="90"><template #default="{ row }">{{ channelTypeLabel(row.type) }}</template></el-table-column>
				<el-table-column label="状态" width="80"><template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag></template></el-table-column>
				<el-table-column label="服务器 / Webhook" min-width="220" show-overflow-tooltip>
					<template #default="{ row }">{{ enumNumber(row.type, channelTypeNames) === 0 ? `${row.server || '-'}:${row.port || '-'}` : row.webhookUrl }}</template>
				</el-table-column>
				<el-table-column prop="recipients" label="收件人" min-width="160" show-overflow-tooltip />
				<el-table-column label="密钥" width="90"><template #default="{ row }"><el-tag :type="row.hasSecret ? 'success' : 'info'" size="small">{{ row.hasSecret ? '已配置' : '未配置' }}</el-tag></template></el-table-column>
				<el-table-column prop="remark" label="备注" min-width="140" show-overflow-tooltip />
				<el-table-column label="操作" width="190" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openChannelEdit(row)">编辑</el-button>
						<el-button link type="warning" :loading="testingId === row.id" @click="testChannel(row)">测试</el-button>
						<el-button link type="danger" @click="removeChannel(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>
			<el-pagination
				v-model:current-page="channelQuery.page"
				v-model:page-size="channelQuery.pageSize"
				class="iot-pager"
				:total="channelTotal"
				layout="total, prev, pager, next"
				@current-change="loadChannelPage"
			/>
		</el-drawer>

		<el-dialog v-model="channelFormVisible" :title="channelForm.id ? '编辑通知渠道' : '新增通知渠道'" width="620px" append-to-body destroy-on-close>
			<el-form :model="channelForm" label-width="110px">
				<el-form-item label="渠道名称" required><el-input v-model="channelForm.name" maxlength="128" /></el-form-item>
				<el-form-item label="渠道类型">
					<el-radio-group v-model="channelForm.type">
						<el-radio-button v-for="item in channelTypeOptions" :key="item.value" :value="item.value">{{ item.label }}</el-radio-button>
					</el-radio-group>
				</el-form-item>
				<el-form-item label="是否启用"><el-switch v-model="channelForm.enabled" /></el-form-item>
				<template v-if="channelForm.type === 0">
					<el-form-item label="SMTP 服务器" required><el-input v-model="channelForm.server" placeholder="如 smtp.example.com" /></el-form-item>
					<el-form-item label="SMTP 端口" required><el-input-number v-model="channelForm.port" :min="1" :max="65535" style="width: 180px" /></el-form-item>
					<el-form-item label="启用 SSL"><el-switch v-model="channelForm.useSsl" /></el-form-item>
					<el-form-item label="账号"><el-input v-model="channelForm.username" autocomplete="off" /></el-form-item>
					<el-form-item label="发件人" required><el-input v-model="channelForm.sender" placeholder="发件人邮箱或显示地址" /></el-form-item>
					<el-form-item label="收件人" required><el-input v-model="channelForm.recipients" type="textarea" :rows="2" placeholder="多个收件人以逗号或分号分隔" /></el-form-item>
				</template>
				<el-form-item v-else label="Webhook" required><el-input v-model="channelForm.webhookUrl" placeholder="请输入完整 HTTP(S) Webhook 地址" /></el-form-item>
				<el-form-item :label="channelForm.type === 0 ? 'SMTP 密码' : '签名密钥'">
					<el-input
						v-model="secretInput"
						type="password"
						show-password
						autocomplete="new-password"
						:placeholder="channelForm.id && channelForm.hasSecret ? '留空保留原密钥，输入新值则替换' : '请输入密钥（可选）'"
						@input="secretCleared = false"
					/>
					<div v-if="channelForm.id" class="secret-actions">
						<span>{{ secretCleared ? '保存后将清除已保存密钥' : channelForm.hasSecret ? '当前已保存密钥' : '当前未配置密钥' }}</span>
						<el-button v-if="channelForm.hasSecret && !secretCleared" link type="danger" @click="clearSecret">清除密钥</el-button>
						<el-button v-if="secretCleared" link type="primary" @click="secretCleared = false">撤销清除</el-button>
					</div>
				</el-form-item>
				<el-form-item label="备注"><el-input v-model="channelForm.remark" type="textarea" :rows="2" maxlength="512" /></el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="channelFormVisible = false">取消</el-button>
				<el-button type="primary" :loading="channelSaving" @click="saveChannel">保存</el-button>
			</template>
		</el-dialog>

		<el-drawer v-model="notificationVisible" title="告警通知日志" size="88%" destroy-on-close>
			<el-form :inline="true" @submit.prevent>
				<el-form-item label="事件 ID"><el-input v-model="notificationQuery.eventId" clearable placeholder="事件 ID" style="width: 160px" /></el-form-item>
				<el-form-item label="渠道">
					<el-select v-model="notificationQuery.channelId" clearable filterable placeholder="全部渠道" style="width: 180px">
						<el-option v-for="item in channels" :key="item.id" :label="item.name" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="结果">
					<el-select v-model="notificationQuery.isSuccess" clearable placeholder="全部" style="width: 110px">
						<el-option label="成功" :value="true" /><el-option label="失败" :value="false" />
					</el-select>
				</el-form-item>
				<el-form-item><el-button type="primary" @click="searchNotifications">查询</el-button><el-button @click="resetNotifications">重置</el-button></el-form-item>
			</el-form>
			<el-table v-loading="notificationLoading" :data="notificationRows" border stripe>
				<el-table-column type="expand">
					<template #default="{ row }">
						<div class="log-detail"><div><b>通知内容：</b><pre>{{ row.content || '-' }}</pre></div><div><b>响应或错误：</b><pre>{{ row.response || '-' }}</pre></div></div>
					</template>
				</el-table-column>
				<el-table-column prop="sendTime" label="发送时间" width="170" />
				<el-table-column prop="channelName" label="渠道" min-width="130" />
				<el-table-column label="渠道类型" width="90"><template #default="{ row }">{{ channelTypeLabel(row.channelType) }}</template></el-table-column>
				<el-table-column label="通知类型" width="100"><template #default="{ row }"><el-tag effect="plain">{{ notificationTypeLabel(row.notificationType) }}</el-tag></template></el-table-column>
				<el-table-column prop="eventId" label="事件 ID" width="110"><template #default="{ row }">{{ row.eventId || '-' }}</template></el-table-column>
				<el-table-column label="结果" width="80"><template #default="{ row }"><el-tag :type="row.isSuccess ? 'success' : 'danger'">{{ row.isSuccess ? '成功' : '失败' }}</el-tag></template></el-table-column>
				<el-table-column prop="content" label="通知内容" min-width="240" show-overflow-tooltip />
				<el-table-column prop="response" label="响应或错误" min-width="200" show-overflow-tooltip />
			</el-table>
			<el-pagination
				v-model:current-page="notificationQuery.page"
				v-model:page-size="notificationQuery.pageSize"
				class="iot-pager"
				:total="notificationTotal"
				:page-sizes="[10, 20, 50, 100]"
				layout="total, sizes, prev, pager, next"
				@current-change="loadNotifications"
				@size-change="onNotificationSizeChange"
			/>
		</el-drawer>
	</div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotAlarmChannelApi, iotAlarmRuleApi, iotDeviceApi, iotDeviceVariableApi } from '/@/api/iot';
import { getAPI } from '/@/utils/axios-utils';
import { SysOrgApi } from '/@/api-services/api';
import { useUserInfo } from '/@/stores/userInfo';

const severityOptions = [
	{ value: 0, label: '提示', type: 'info' },
	{ value: 1, label: '一般', type: 'warning' },
	{ value: 2, label: '严重', type: 'danger' },
	{ value: 3, label: '紧急', type: 'danger' },
];
const operatorOptions = [
	{ value: 0, label: '> 大于' },
	{ value: 1, label: '>= 大于等于' },
	{ value: 2, label: '< 小于' },
	{ value: 3, label: '<= 小于等于' },
	{ value: 4, label: '= 等于' },
	{ value: 5, label: '!= 不等于' },
	{ value: 6, label: '区间内' },
	{ value: 7, label: '区间外' },
	{ value: 8, label: '状态 =' },
	{ value: 9, label: '状态 !=' },
	{ value: 10, label: '变化率 >' },
	{ value: 11, label: '变化率 <' },
	{ value: 12, label: '绝对变化率 >' },
];
const variableStatusOptions = [
	{ value: 0, label: '正常' },
	{ value: 1, label: '地址错误' },
	{ value: 2, label: '方法错误' },
	{ value: 3, label: '表达式错误' },
	{ value: 4, label: '异常' },
	{ value: 5, label: '未采集' },
	{ value: 6, label: '自定义1' },
	{ value: 7, label: '自定义2' },
	{ value: 8, label: '自定义3' },
	{ value: 9, label: '自定义4' },
	{ value: 10, label: '自定义5' },
];
const channelTypeOptions = [
	{ value: 0, label: '邮件' },
	{ value: 1, label: '钉钉' },
	{ value: 2, label: '飞书' },
];
const notificationTypeOptions = [
	{ value: 0, label: '触发' },
	{ value: 1, label: '提醒' },
	{ value: 2, label: '恢复' },
	{ value: 3, label: '测试' },
];
const severityNames = { Info: 0, Warning: 1, Critical: 2, Emergency: 3 };
const operatorNames = {
	GreaterThan: 0,
	GreaterOrEqual: 1,
	LessThan: 2,
	LessOrEqual: 3,
	Equal: 4,
	NotEqual: 5,
	Between: 6,
	Outside: 7,
	StatusEqual: 8,
	StatusNotEqual: 9,
	RateGreater: 10,
	RateLess: 11,
	RateAbsoluteGreater: 12,
};
const variableStatusNames = { Good: 0, AddressError: 1, MethodError: 2, ExpressionError: 3, Bad: 4, UnKnow: 5, Custome1: 6, Custome2: 7, Custome3: 8, Custome4: 9, Custome5: 10 };
const channelTypeNames = { Email: 0, DingTalk: 1, Feishu: 2 };
const notificationTypeNames = { Trigger: 0, Reminder: 1, Recovery: 2, Test: 3 };
const orgTreeProps = { label: 'name', children: 'children', disabled: 'disabled' };
const userStore = useUserInfo();
const isSuperAdmin = computed(() => Number(userStore.userInfos.accountType) === 999);

const loading = ref(false);
const saving = ref(false);
const tableData = ref([]);
const total = ref(0);
const orgTree = ref([]);
const devices = ref([]);
const variables = ref([]);
const channels = ref([]);
const ruleVisible = ref(false);
const query = reactive({ keyword: '', deviceId: null, orgId: null, severity: null, enabled: null, page: 1, pageSize: 20 });
const form = reactive({ id: 0, name: '', deviceId: null, severity: 1, combineMode: 0, enabled: true, triggerSamples: 1, recoverySamples: 1, suppressSeconds: 0, channelIds: [], remark: '', conditions: [] });
let conditionKey = 0;

const channelVisible = ref(false);
const channelLoading = ref(false);
const channelSaving = ref(false);
const channelFormVisible = ref(false);
const channelRows = ref([]);
const channelTotal = ref(0);
const testingId = ref(null);
const channelQuery = reactive({ keyword: '', page: 1, pageSize: 10 });
const channelForm = reactive({ id: 0, name: '', type: 0, enabled: true, webhookUrl: '', server: '', port: 465, useSsl: true, username: '', sender: '', recipients: '', remark: '', hasSecret: false });
const secretInput = ref('');
const secretCleared = ref(false);

const notificationVisible = ref(false);
const notificationLoading = ref(false);
const notificationRows = ref([]);
const notificationTotal = ref(0);
const notificationQuery = reactive({ eventId: null, channelId: null, isSuccess: null, page: 1, pageSize: 20 });

const deviceOptions = computed(() => devices.value.filter((item) => Number(item.deviceTypeEnum) !== 0 && item.deviceTypeEnum !== 'Group'));
const enumNumber = (value, names = {}) => {
	if (value === null || value === undefined || value === '') return null;
	if (typeof value === 'string' && Object.prototype.hasOwnProperty.call(names, value)) return names[value];
	const number = Number(value);
	return Number.isNaN(number) ? value : number;
};
const severityLabel = (value) => severityOptions.find((item) => item.value === enumNumber(value, severityNames))?.label ?? value ?? '-';
const severityTag = (value) => severityOptions.find((item) => item.value === enumNumber(value, severityNames))?.type || 'info';
const combineLabel = (value) => (enumNumber(value, { All: 0, Any: 1 }) === 1 ? 'OR' : 'AND');
const channelTypeLabel = (value) => channelTypeOptions.find((item) => item.value === enumNumber(value, channelTypeNames))?.label ?? value ?? '-';
const notificationTypeLabel = (value) => notificationTypeOptions.find((item) => item.value === enumNumber(value, notificationTypeNames))?.label ?? value ?? '-';
const operatorLabel = (value) => operatorOptions.find((item) => item.value === enumNumber(value, operatorNames))?.label ?? value;
const variableStatusLabel = (value) => variableStatusOptions.find((item) => item.value === enumNumber(value, variableStatusNames))?.label ?? value;
const isRangeOperator = (value) => [6, 7].includes(enumNumber(value, operatorNames));
const isStatusOperator = (value) => [8, 9].includes(enumNumber(value, operatorNames));
const channelNames = (ids) => {
	if (!ids?.length) return '未配置';
	return ids.map((id) => channels.value.find((item) => item.id === id)?.name || `渠道 ${id}`).join('、');
};
const conditionText = (condition) => {
	const operator = enumNumber(condition.operator, operatorNames);
	const variableName = condition.variableName || variables.value.find((item) => item.id === condition.variableId)?.name || `变量 ${condition.variableId}`;
	let comparison = operatorLabel(operator);
	if ([8, 9].includes(operator)) comparison += ` ${variableStatusLabel(condition.status)}`;
	else if ([6, 7].includes(operator)) comparison += ` [${condition.value}, ${condition.secondValue}]`;
	else comparison += ` ${condition.value}`;
	return `${variableName} ${comparison}`;
};

const loadOrgs = async () => {
	const res = await getAPI(SysOrgApi).apiSysOrgTreeGet(0);
	orgTree.value = res.data.result || [];
};
const loadDevices = async () => {
	const res = await iotDeviceApi.list();
	devices.value = res.data.result || [];
};
const loadChannels = async () => {
	const res = await iotAlarmChannelApi.options();
	channels.value = res.data.result || [];
};
const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotAlarmRuleApi.page({ ...query });
		tableData.value = res.data.result.items || [];
		total.value = res.data.result.total || 0;
	} finally {
		loading.value = false;
	}
};
const search = () => {
	query.page = 1;
	loadData();
};
const resetQuery = () => {
	Object.assign(query, { keyword: '', deviceId: null, orgId: null, severity: null, enabled: null, page: 1 });
	loadData();
};
const onPageSizeChange = () => {
	query.page = 1;
	loadData();
};
const newCondition = () => ({ key: ++conditionKey, id: 0, variableId: null, operator: 0, value: null, secondValue: null, status: null, sort: 0 });
const resetForm = () => {
	Object.assign(form, { id: 0, name: '', deviceId: null, severity: 1, combineMode: 0, enabled: true, triggerSamples: 1, recoverySamples: 1, suppressSeconds: 0, channelIds: [], remark: '', conditions: [newCondition()] });
	variables.value = [];
};
const loadVariables = async (deviceId) => {
	variables.value = [];
	if (!deviceId) return;
	const res = await iotDeviceVariableApi.list(deviceId);
	variables.value = res.data.result || [];
};
const openAdd = () => {
	resetForm();
	ruleVisible.value = true;
};
const openEdit = async (row) => {
	const res = await iotAlarmRuleApi.detail(row.id);
	const detail = res.data.result || {};
	Object.assign(form, {
		id: detail.id,
		name: detail.name,
		deviceId: detail.deviceId,
		severity: enumNumber(detail.severity, severityNames) ?? 1,
		combineMode: enumNumber(detail.combineMode, { All: 0, Any: 1 }) ?? 0,
		enabled: detail.enabled,
		triggerSamples: detail.triggerSamples,
		recoverySamples: detail.recoverySamples,
		suppressSeconds: detail.suppressSeconds,
		channelIds: [...(detail.channelIds || [])],
		remark: detail.remark || '',
		conditions: (detail.conditions || []).map((item, index) => ({
			...newCondition(),
			id: item.id,
			variableId: item.variableId,
			operator: enumNumber(item.operator, operatorNames) ?? 0,
			value: item.value,
			secondValue: item.secondValue,
			status: enumNumber(item.status, variableStatusNames),
			sort: item.sort ?? index,
		})),
	});
	if (!form.conditions.length) form.conditions = [newCondition()];
	await loadVariables(form.deviceId);
	ruleVisible.value = true;
};
const onFormDeviceChange = async () => {
	form.conditions = [newCondition()];
	await loadVariables(form.deviceId);
};
const addCondition = () => form.conditions.push(newCondition());
const removeCondition = (index) => form.conditions.splice(index, 1);
const onOperatorChange = (condition) => {
	condition.value = null;
	condition.secondValue = null;
	condition.status = null;
};
const saveRule = async () => {
	if (!form.name.trim()) return ElMessage.warning('请输入规则名称');
	if (!form.deviceId) return ElMessage.warning('请选择采集设备');
	if (!form.conditions.length) return ElMessage.warning('请至少添加一个告警条件');
	for (const [index, condition] of form.conditions.entries()) {
		if (!condition.variableId) return ElMessage.warning(`请选择第 ${index + 1} 个条件的变量`);
		const operator = enumNumber(condition.operator, operatorNames);
		if ([8, 9].includes(operator)) {
			if (condition.status === null || condition.status === undefined) return ElMessage.warning(`请选择第 ${index + 1} 个条件的变量状态`);
		} else {
			if (condition.value === null || condition.value === undefined) return ElMessage.warning(`请填写第 ${index + 1} 个条件的阈值`);
			if ([6, 7].includes(operator) && (condition.secondValue === null || condition.secondValue === undefined)) return ElMessage.warning(`请填写第 ${index + 1} 个条件的区间上限`);
		}
	}
	saving.value = true;
	try {
		const device = devices.value.find((item) => item.id === form.deviceId);
		const payload = {
			id: form.id,
			name: form.name.trim(),
			orgId: device?.orgId || 0,
			deviceId: form.deviceId,
			severity: form.severity,
			combineMode: form.combineMode,
			enabled: form.enabled,
			triggerSamples: form.triggerSamples,
			recoverySamples: form.recoverySamples,
			suppressSeconds: form.suppressSeconds,
			channelIds: form.channelIds,
			remark: form.remark,
			conditions: form.conditions.map((item, index) => ({
				id: 0,
				ruleId: form.id,
				variableId: item.variableId,
				operator: enumNumber(item.operator, operatorNames),
				value: isStatusOperator(item.operator) ? null : item.value,
				secondValue: isRangeOperator(item.operator) ? item.secondValue : null,
				status: isStatusOperator(item.operator) ? item.status : null,
				sort: index,
			})),
		};
		if (form.id) await iotAlarmRuleApi.update(payload);
		else await iotAlarmRuleApi.add(payload);
		ElMessage.success('保存成功');
		ruleVisible.value = false;
		await loadData();
	} finally {
		saving.value = false;
	}
};
const removeRule = async (row) => {
	await ElMessageBox.confirm(`确认删除告警规则「${row.name}」吗？历史事件将保留。`, '提示', { type: 'warning' });
	await iotAlarmRuleApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
};

const openChannelManager = async () => {
	channelVisible.value = true;
	await loadChannelPage();
};
const loadChannelPage = async () => {
	channelLoading.value = true;
	try {
		const res = await iotAlarmChannelApi.page({ ...channelQuery });
		channelRows.value = res.data.result.items || [];
		channelTotal.value = res.data.result.total || 0;
	} finally {
		channelLoading.value = false;
	}
};
const searchChannels = () => {
	channelQuery.page = 1;
	loadChannelPage();
};
const resetChannelForm = () => {
	Object.assign(channelForm, { id: 0, name: '', type: 0, enabled: true, webhookUrl: '', server: '', port: 465, useSsl: true, username: '', sender: '', recipients: '', remark: '', hasSecret: false });
	secretInput.value = '';
	secretCleared.value = false;
};
const openChannelAdd = () => {
	resetChannelForm();
	channelFormVisible.value = true;
};
const openChannelEdit = (row) => {
	Object.assign(channelForm, {
		id: row.id,
		name: row.name,
		type: enumNumber(row.type, channelTypeNames) ?? 0,
		enabled: row.enabled,
		webhookUrl: row.webhookUrl || '',
		server: row.server || '',
		port: row.port || 465,
		useSsl: row.useSsl,
		username: row.username || '',
		sender: row.sender || '',
		recipients: row.recipients || '',
		remark: row.remark || '',
		hasSecret: row.hasSecret,
	});
	secretInput.value = '';
	secretCleared.value = false;
	channelFormVisible.value = true;
};
const clearSecret = () => {
	secretInput.value = '';
	secretCleared.value = true;
};
const saveChannel = async () => {
	if (!channelForm.name.trim()) return ElMessage.warning('请输入渠道名称');
	if (channelForm.type === 0) {
		if (!channelForm.server || !channelForm.port || !channelForm.sender || !channelForm.recipients) return ElMessage.warning('请完整填写 SMTP 服务器、端口、发件人和收件人');
	} else if (!channelForm.webhookUrl) return ElMessage.warning('请输入 Webhook 地址');
	channelSaving.value = true;
	try {
		const payload = { ...channelForm, name: channelForm.name.trim(), secret: channelForm.id ? (secretCleared.value ? '' : secretInput.value || null) : secretInput.value };
		delete payload.hasSecret;
		if (channelForm.id) await iotAlarmChannelApi.update(payload);
		else await iotAlarmChannelApi.add(payload);
		ElMessage.success('保存成功');
		channelFormVisible.value = false;
		await Promise.all([loadChannelPage(), loadChannels()]);
	} finally {
		channelSaving.value = false;
	}
};
const removeChannel = async (row) => {
	await ElMessageBox.confirm(`确认删除通知渠道「${row.name}」吗？`, '提示', { type: 'warning' });
	await iotAlarmChannelApi.delete(row.id);
	ElMessage.success('删除成功');
	await Promise.all([loadChannelPage(), loadChannels()]);
};
const testChannel = async (row) => {
	testingId.value = row.id;
	try {
		const res = await iotAlarmChannelApi.test(row.id);
		const result = res.data.result || {};
		if (result.isSuccess) ElMessage.success('测试通知发送成功');
		else ElMessage.error(result.response || '测试通知发送失败');
	} finally {
		testingId.value = null;
	}
};

const openNotifications = async () => {
	notificationVisible.value = true;
	await loadNotifications();
};
const loadNotifications = async () => {
	notificationLoading.value = true;
	try {
		const payload = { ...notificationQuery, eventId: notificationQuery.eventId ? Number(notificationQuery.eventId) : null };
		const res = await iotAlarmChannelApi.notificationPage(payload);
		notificationRows.value = res.data.result.items || [];
		notificationTotal.value = res.data.result.total || 0;
	} finally {
		notificationLoading.value = false;
	}
};
const searchNotifications = () => {
	notificationQuery.page = 1;
	loadNotifications();
};
const resetNotifications = () => {
	Object.assign(notificationQuery, { eventId: null, channelId: null, isSuccess: null, page: 1 });
	loadNotifications();
};
const onNotificationSizeChange = () => {
	notificationQuery.page = 1;
	loadNotifications();
};

onMounted(async () => {
	await Promise.all([loadOrgs(), loadDevices(), loadChannels()]);
	await loadData();
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.iot-search {
	margin-bottom: 8px;
}
.iot-pager {
	margin-top: 16px;
	justify-content: flex-end;
}
.condition-summary {
	display: flex;
	flex-wrap: wrap;
	gap: 4px 8px;
	font-size: 12px;
}
.condition-summary .el-tag {
	flex-shrink: 0;
}
.form-hint {
	margin-left: 12px;
	color: var(--el-text-color-secondary);
	font-size: 12px;
}
.condition-alert {
	margin-bottom: 12px;
}
.condition-row {
	display: flex;
	align-items: center;
	gap: 8px;
	margin-bottom: 10px;
}
.condition-index {
	width: 24px;
	flex: 0 0 24px;
	text-align: center;
	color: var(--el-text-color-secondary);
}
.condition-variable {
	width: 180px;
}
.condition-operator {
	width: 150px;
}
.condition-value {
	width: 130px;
}
.condition-second {
	width: 110px;
}
.drawer-toolbar {
	display: flex;
	gap: 8px;
	margin-bottom: 12px;
}
.secret-actions {
	display: flex;
	align-items: center;
	width: 100%;
	justify-content: space-between;
	color: var(--el-text-color-secondary);
	font-size: 12px;
	line-height: 24px;
}
.log-detail {
	display: grid;
	grid-template-columns: 1fr 1fr;
	gap: 16px;
	padding: 8px 24px;
}
.log-detail pre {
	white-space: pre-wrap;
	word-break: break-all;
	font-family: inherit;
	color: var(--el-text-color-regular);
}
@media (max-width: 900px) {
	.condition-row {
		flex-wrap: wrap;
	}
	.condition-variable,
	.condition-operator,
	.condition-value,
	.condition-second {
		width: calc(50% - 36px);
	}
}
</style>
