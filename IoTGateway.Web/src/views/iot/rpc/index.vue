<template>
	<div class="iot-page">
		<el-card shadow="never" class="invoke-card">
			<template #header>RPC 写入调试</template>
			<el-form :inline="true" :model="invokeForm">
				<el-form-item label="设备">
					<el-select v-model="invokeForm.deviceName" filterable placeholder="请选择设备" style="width: 200px">
						<el-option v-for="item in devices" :key="item.id" :label="item.deviceName" :value="item.deviceName" />
					</el-select>
				</el-form-item>
				<el-form-item label="方法">
					<el-input v-model="invokeForm.method" placeholder="write" style="width: 130px" />
				</el-form-item>
				<el-form-item label="参数">
					<el-input v-model="invokeForm.params" placeholder='{"temperature":25}' style="width: 300px" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :loading="invoking" @click="invoke">执行</el-button>
				</el-form-item>
			</el-form>
		</el-card>

		<el-card shadow="never" class="log-card">
			<el-form :inline="true" :model="query" class="iot-search">
				<el-form-item label="方法名">
					<el-input v-model="query.keyword" placeholder="请输入 RPC 方法名" clearable @keyup.enter="loadData" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="loadData">查询</el-button>
					<el-button type="danger" :disabled="!selectedIds.length" @click="batchRemove">批量删除</el-button>
					<el-button @click="exportLogs">导出日志</el-button>
					<el-button type="danger" plain @click="clearAll">清空日志</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe empty-text="暂无 RPC 日志" @selection-change="onSelectionChange">
				<el-table-column type="selection" width="46" />
				<el-table-column label="发起方" width="100">
					<template #default="{ row }">{{ Number(row.rpcSide) === 2 ? '客户端' : '服务端' }}</template>
				</el-table-column>
				<el-table-column label="设备" min-width="130">
					<template #default="{ row }">{{ deviceName(row.deviceId) }}</template>
				</el-table-column>
				<el-table-column prop="method" label="方法" min-width="160" />
				<el-table-column prop="startTime" label="开始时间" width="180" />
				<el-table-column prop="endTime" label="结束时间" width="180" />
				<el-table-column label="结果" width="90">
					<template #default="{ row }">
						<el-tag :type="row.isSuccess ? 'success' : 'danger'">{{ row.isSuccess ? '成功' : '失败' }}</el-tag>
					</template>
				</el-table-column>
				<el-table-column prop="params" label="请求参数" min-width="200" show-overflow-tooltip />
				<el-table-column prop="description" label="描述" min-width="160" show-overflow-tooltip />
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openDetail(row)">详情</el-button>
						<el-button link type="danger" @click="remove(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>

			<el-pagination
				class="iot-pager"
				v-model:current-page="query.page"
				v-model:page-size="query.pageSize"
				:total="total"
				:page-sizes="[10, 20, 50]"
				layout="total, sizes, prev, pager, next"
				@current-change="loadData"
				@size-change="loadData"
			/>
		</el-card>

		<el-dialog v-model="detailVisible" title="RPC 日志详情" width="640px" destroy-on-close>
			<el-descriptions v-if="detail" :column="2" border>
				<el-descriptions-item label="发起方">{{ Number(detail.rpcSide) === 2 ? '客户端' : '服务端' }}</el-descriptions-item>
				<el-descriptions-item label="设备">{{ deviceName(detail.deviceId) }}</el-descriptions-item>
				<el-descriptions-item label="方法">{{ detail.method }}</el-descriptions-item>
				<el-descriptions-item label="结果">
					<el-tag :type="detail.isSuccess ? 'success' : 'danger'">{{ detail.isSuccess ? '成功' : '失败' }}</el-tag>
				</el-descriptions-item>
				<el-descriptions-item label="开始时间">{{ detail.startTime }}</el-descriptions-item>
				<el-descriptions-item label="结束时间">{{ detail.endTime }}</el-descriptions-item>
				<el-descriptions-item label="请求参数" :span="2"><pre class="detail-pre">{{ detail.params }}</pre></el-descriptions-item>
				<el-descriptions-item label="描述" :span="2">{{ detail.description }}</el-descriptions-item>
			</el-descriptions>
			<template #footer>
				<el-button @click="detailVisible = false">关闭</el-button>
			</template>
		</el-dialog>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotDeviceApi, iotRpcApi, iotRpcLogApi } from '/@/api/iot';

const loading = ref(false);
const tableData = ref([]);
const total = ref(0);
const devices = ref([]);
const invoking = ref(false);
const invokeForm = reactive({ deviceName: '', method: 'write', params: '{"temperature":25}' });
const query = reactive({ keyword: '', page: 1, pageSize: 20 });
const selectedIds = ref([]);
const detailVisible = ref(false);
const detail = ref(null);

const deviceName = (deviceId) => {
	if (!deviceId) return '-';
	return devices.value.find((x) => Number(x.id) === Number(deviceId))?.deviceName || deviceId;
};

const onSelectionChange = (rows) => {
	selectedIds.value = rows.map((x) => x.id);
};

const batchRemove = async () => {
	await ElMessageBox.confirm(`确认删除选中的 ${selectedIds.value.length} 条 RPC 日志吗？`, '提示', { type: 'warning' });
	await iotRpcLogApi.batchDelete(selectedIds.value);
	ElMessage.success('删除成功');
	await loadData();
};

const openDetail = async (row) => {
	const res = await iotRpcLogApi.detail(row.id);
	detail.value = res.data.result || row;
	detailVisible.value = true;
};

const exportLogs = async () => {
	const res = await iotRpcLogApi.export({ keyword: query.keyword });
	const blob = new Blob([res.data], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
	const url = URL.createObjectURL(blob);
	const link = document.createElement('a');
	link.href = url;
	link.download = `rpc_logs_${Date.now()}.xlsx`;
	link.click();
	URL.revokeObjectURL(url);
};

const loadDevices = async () => {
	const res = await iotDeviceApi.page({ page: 1, pageSize: 200 });
	devices.value = (res.data.result.items || []).filter((x) => x.deviceTypeEnum !== 0);
};

const invoke = async () => {
	if (!invokeForm.deviceName) {
		ElMessage.warning('请选择设备');
		return;
	}
	let params = {};
	try {
		params = invokeForm.params ? JSON.parse(invokeForm.params) : {};
	} catch {
		ElMessage.error('参数必须是合法 JSON');
		return;
	}
	invoking.value = true;
	try {
		const res = await iotRpcApi.invoke({
			deviceName: invokeForm.deviceName,
			method: invokeForm.method,
			requestId: `${Date.now()}`,
			params,
		});
		const result = res.data.result || {};
		if (result.isSuccess) ElMessage.success(`执行成功：${result.description || ''}`);
		else ElMessage.error(`执行失败：${result.description || '未知错误'}`);
		await loadData();
	} finally {
		invoking.value = false;
	}
};

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotRpcLogApi.page({ ...query });
		tableData.value = res.data.result.items || [];
		total.value = res.data.result.total || 0;
	} finally {
		loading.value = false;
	}
};

const remove = async (row) => {
	await ElMessageBox.confirm('确认删除该条 RPC 日志吗？', '提示', { type: 'warning' });
	await iotRpcLogApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
};

const clearAll = async () => {
	await ElMessageBox.confirm('确认清空全部 RPC 日志吗？', '提示', { type: 'warning' });
	await iotRpcLogApi.clear();
	ElMessage.success('已清空');
	await loadData();
};

onMounted(() => {
	loadData();
	loadDevices();
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.invoke-card {
	margin-bottom: 12px;
}
.iot-search {
	margin-bottom: 8px;
}
.iot-pager {
	margin-top: 16px;
	justify-content: flex-end;
}
.detail-pre {
	margin: 0;
	white-space: pre-wrap;
	word-break: break-all;
	font-family: monospace;
}
</style>
