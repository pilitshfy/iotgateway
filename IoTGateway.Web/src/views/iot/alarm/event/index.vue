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
					<el-select v-model="query.deviceId" placeholder="全部设备" filterable clearable style="width: 180px">
						<el-option v-for="item in deviceOptions" :key="item.id" :label="item.deviceName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="关键词"><el-input v-model="query.keyword" placeholder="规则、设备或描述" clearable style="width: 180px" @keyup.enter="search" /></el-form-item>
				<el-form-item label="级别">
					<el-select v-model="query.severity" placeholder="全部" clearable style="width: 105px">
						<el-option v-for="item in severityOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="状态">
					<el-select v-model="query.status" placeholder="全部" clearable style="width: 110px">
						<el-option v-for="item in statusOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="触发时间">
					<el-date-picker
						v-model="timeRange"
						type="datetimerange"
						range-separator="至"
						start-placeholder="开始时间"
						end-placeholder="结束时间"
						value-format="YYYY-MM-DD HH:mm:ss"
						style="width: 350px"
					/>
				</el-form-item>
				<el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetQuery">重置</el-button><el-button @click="loadData">刷新</el-button></el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe>
				<el-table-column prop="id" label="事件 ID" width="100" />
				<el-table-column prop="ruleName" label="告警规则" min-width="150" show-overflow-tooltip />
				<el-table-column prop="deviceName" label="设备" min-width="130" show-overflow-tooltip />
				<el-table-column label="级别" width="85"><template #default="{ row }"><el-tag :type="severityTag(row.severity)">{{ severityLabel(row.severity) }}</el-tag></template></el-table-column>
				<el-table-column label="状态" width="95"><template #default="{ row }"><el-tag :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag></template></el-table-column>
				<el-table-column prop="triggerTime" label="首次触发" width="165" />
				<el-table-column prop="lastTriggerTime" label="最近触发" width="165" />
				<el-table-column prop="recoverTime" label="恢复时间" width="165"><template #default="{ row }">{{ row.recoverTime || '-' }}</template></el-table-column>
				<el-table-column prop="triggerCount" label="触发次数" width="90" align="center" />
				<el-table-column prop="triggerValue" label="触发值" min-width="120" show-overflow-tooltip><template #default="{ row }">{{ row.triggerValue || '-' }}</template></el-table-column>
				<el-table-column prop="message" label="事件描述" min-width="220" show-overflow-tooltip />
				<el-table-column label="操作" width="190" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openDetail(row)">详情</el-button>
						<el-button v-if="eventStatus(row.status) === 0" link type="warning" @click="openHandle(row, 'ack')">确认</el-button>
						<el-button v-if="eventStatus(row.status) === 2" link type="success" @click="openHandle(row, 'close')">闭环</el-button>
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

		<el-drawer v-model="detailVisible" title="告警事件详情" size="620px">
			<el-descriptions :column="2" border>
				<el-descriptions-item label="事件 ID">{{ detail.id }}</el-descriptions-item>
				<el-descriptions-item label="规则 ID">{{ detail.ruleId }}</el-descriptions-item>
				<el-descriptions-item label="告警规则" :span="2">{{ detail.ruleName }}</el-descriptions-item>
				<el-descriptions-item label="设备" :span="2">{{ detail.deviceName }}（{{ detail.deviceId }}）</el-descriptions-item>
				<el-descriptions-item label="级别"><el-tag :type="severityTag(detail.severity)">{{ severityLabel(detail.severity) }}</el-tag></el-descriptions-item>
				<el-descriptions-item label="状态"><el-tag :type="statusTag(detail.status)">{{ statusLabel(detail.status) }}</el-tag></el-descriptions-item>
				<el-descriptions-item label="首次触发">{{ detail.triggerTime }}</el-descriptions-item>
				<el-descriptions-item label="最近触发">{{ detail.lastTriggerTime }}</el-descriptions-item>
				<el-descriptions-item label="恢复时间">{{ detail.recoverTime || '-' }}</el-descriptions-item>
				<el-descriptions-item label="触发次数">{{ detail.triggerCount }}</el-descriptions-item>
				<el-descriptions-item label="触发值" :span="2">{{ detail.triggerValue || '-' }}</el-descriptions-item>
				<el-descriptions-item label="事件描述" :span="2"><div class="long-text">{{ detail.message || '-' }}</div></el-descriptions-item>
				<el-descriptions-item label="确认时间">{{ detail.ackTime || '-' }}</el-descriptions-item>
				<el-descriptions-item label="确认人">{{ detail.ackUserName || '-' }}</el-descriptions-item>
				<el-descriptions-item label="闭环时间">{{ detail.closeTime || '-' }}</el-descriptions-item>
				<el-descriptions-item label="闭环人">{{ detail.closeUserName || '-' }}</el-descriptions-item>
				<el-descriptions-item label="最近通知">{{ detail.lastNotifyTime || '-' }}</el-descriptions-item>
				<el-descriptions-item label="处理备注" :span="2"><div class="long-text">{{ detail.handleRemark || '-' }}</div></el-descriptions-item>
			</el-descriptions>
			<div class="detail-actions">
				<el-button v-if="eventStatus(detail.status) === 0" type="warning" @click="openHandle(detail, 'ack')">确认告警</el-button>
				<el-button v-if="eventStatus(detail.status) === 2" type="success" @click="openHandle(detail, 'close')">闭环告警</el-button>
			</div>
		</el-drawer>

		<el-dialog v-model="handleVisible" :title="handleType === 'ack' ? '确认活动告警' : '闭环已恢复告警'" width="520px" destroy-on-close>
			<el-alert :title="handleType === 'ack' ? '确认表示已知晓并跟进该活动告警，不会将告警恢复。' : '仅已恢复的告警可以闭环，闭环后事件处理完成。'" :type="handleType === 'ack' ? 'warning' : 'success'" :closable="false" class="handle-alert" />
			<el-form label-width="90px">
				<el-form-item label="告警规则">{{ handleRow.ruleName }}</el-form-item>
				<el-form-item label="处理备注"><el-input v-model="handleRemark" type="textarea" :rows="4" maxlength="2048" show-word-limit placeholder="请输入处理备注（可选）" /></el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="handleVisible = false">取消</el-button>
				<el-button :type="handleType === 'ack' ? 'warning' : 'success'" :loading="handling" @click="submitHandle">{{ handleType === 'ack' ? '确认告警' : '确认闭环' }}</el-button>
			</template>
		</el-dialog>
	</div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue';
import { ElMessage } from 'element-plus';
import { iotAlarmEventApi, iotDeviceApi } from '/@/api/iot';
import { getAPI } from '/@/utils/axios-utils';
import { SysOrgApi } from '/@/api-services/api';

const severityOptions = [
	{ value: 0, label: '提示', type: 'info' },
	{ value: 1, label: '一般', type: 'warning' },
	{ value: 2, label: '严重', type: 'danger' },
	{ value: 3, label: '紧急', type: 'danger' },
];
const statusOptions = [
	{ value: 0, label: '活动中', type: 'danger' },
	{ value: 1, label: '已确认', type: 'warning' },
	{ value: 2, label: '已恢复', type: 'success' },
	{ value: 3, label: '已闭环', type: 'info' },
];
const severityNames = { Info: 0, Warning: 1, Critical: 2, Emergency: 3 };
const statusNames = { Active: 0, Acknowledged: 1, Recovered: 2, Closed: 3 };
const orgTreeProps = { label: 'name', children: 'children', disabled: 'disabled' };
const loading = ref(false);
const tableData = ref([]);
const total = ref(0);
const orgTree = ref([]);
const devices = ref([]);
const timeRange = ref([]);
const query = reactive({ keyword: '', deviceId: null, orgId: null, severity: null, status: null, page: 1, pageSize: 20 });
const detailVisible = ref(false);
const detail = ref({});
const handleVisible = ref(false);
const handling = ref(false);
const handleType = ref('ack');
const handleRow = ref({});
const handleRemark = ref('');

const deviceOptions = computed(() => devices.value.filter((item) => Number(item.deviceTypeEnum) !== 0 && item.deviceTypeEnum !== 'Group'));
const enumNumber = (value, names) => {
	if (value === null || value === undefined || value === '') return null;
	if (typeof value === 'string' && Object.prototype.hasOwnProperty.call(names, value)) return names[value];
	const number = Number(value);
	return Number.isNaN(number) ? value : number;
};
const severityLabel = (value) => severityOptions.find((item) => item.value === enumNumber(value, severityNames))?.label ?? value ?? '-';
const severityTag = (value) => severityOptions.find((item) => item.value === enumNumber(value, severityNames))?.type || 'info';
const eventStatus = (value) => enumNumber(value, statusNames);
const statusLabel = (value) => statusOptions.find((item) => item.value === eventStatus(value))?.label ?? value ?? '-';
const statusTag = (value) => statusOptions.find((item) => item.value === eventStatus(value))?.type || 'info';
const toIso = (value) => {
	if (!value) return value;
	const date = new Date(String(value).replace(' ', 'T'));
	return Number.isNaN(date.getTime()) ? value : date.toISOString();
};

const loadOrgs = async () => {
	const res = await getAPI(SysOrgApi).apiSysOrgTreeGet(0);
	orgTree.value = res.data.result || [];
};
const loadDevices = async () => {
	const res = await iotDeviceApi.list();
	devices.value = res.data.result || [];
};
const loadData = async () => {
	loading.value = true;
	try {
		const payload = { ...query };
		if (timeRange.value?.length === 2) {
			payload.startTime = toIso(timeRange.value[0]);
			payload.endTime = toIso(timeRange.value[1]);
		}
		const res = await iotAlarmEventApi.page(payload);
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
	Object.assign(query, { keyword: '', deviceId: null, orgId: null, severity: null, status: null, page: 1 });
	timeRange.value = [];
	loadData();
};
const onPageSizeChange = () => {
	query.page = 1;
	loadData();
};
const openDetail = async (row) => {
	const res = await iotAlarmEventApi.detail(row.id);
	detail.value = res.data.result || {};
	detailVisible.value = true;
};
const openHandle = (row, type) => {
	handleRow.value = row;
	handleType.value = type;
	handleRemark.value = row.handleRemark || '';
	handleVisible.value = true;
};
const submitHandle = async () => {
	handling.value = true;
	try {
		const payload = { id: handleRow.value.id, remark: handleRemark.value };
		if (handleType.value === 'ack') await iotAlarmEventApi.ack(payload);
		else await iotAlarmEventApi.close(payload);
		ElMessage.success(handleType.value === 'ack' ? '告警已确认' : '告警已闭环');
		handleVisible.value = false;
		await loadData();
		if (detailVisible.value && detail.value.id === handleRow.value.id) await openDetail(handleRow.value);
	} finally {
		handling.value = false;
	}
};

onMounted(async () => {
	await Promise.all([loadOrgs(), loadDevices()]);
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
.long-text {
	white-space: pre-wrap;
	word-break: break-word;
}
.detail-actions {
	margin-top: 20px;
	text-align: right;
}
.handle-alert {
	margin-bottom: 16px;
}
</style>
