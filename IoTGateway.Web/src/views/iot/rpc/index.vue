<template>
	<div class="iot-page">
		<el-card shadow="never">
			<el-form :inline="true" :model="query" class="iot-search">
				<el-form-item label="方法名">
					<el-input v-model="query.keyword" placeholder="请输入 RPC 方法名" clearable @keyup.enter="loadData" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="loadData">查询</el-button>
					<el-button type="danger" plain @click="clearAll">清空日志</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe empty-text="暂无 RPC 日志">
				<el-table-column prop="rpcSide" label="发起方" width="100" />
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
				<el-table-column label="操作" width="90" fixed="right">
					<template #default="{ row }">
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
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotRpcLogApi } from '/@/api/iot';

const loading = ref(false);
const tableData = ref([]);
const total = ref(0);
const query = reactive({ keyword: '', page: 1, pageSize: 20 });

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

onMounted(loadData);
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
</style>
