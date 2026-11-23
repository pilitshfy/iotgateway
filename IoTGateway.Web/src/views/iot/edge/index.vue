<template>
	<div class="iot-page">
		<el-card shadow="never" v-loading="loading">
			<template #header>
				<div class="card-header">
					<span>边缘节点</span>
					<el-button type="primary" :icon="Plus" @click="handleAdd">新增节点</el-button>
				</div>
			</template>

			<el-form :inline="true" :model="query" @submit.prevent>
				<el-form-item label="关键字">
					<el-input v-model="query.keyword" placeholder="名称 / 编号" clearable style="width: 200px" @keyup.enter="loadList" />
				</el-form-item>
				<el-form-item label="在线状态">
					<el-select v-model="query.online" placeholder="全部" clearable style="width: 140px">
						<el-option label="在线" :value="true" />
						<el-option label="离线" :value="false" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="onSearch">查询</el-button>
					<el-button @click="onReset">重置</el-button>
				</el-form-item>
			</el-form>

			<el-table :data="list" border stripe style="width: 100%">
				<el-table-column prop="name" label="节点名称" min-width="140" />
				<el-table-column prop="nodeCode" label="节点编号" min-width="160" />
				<el-table-column prop="orgName" label="所属组织" min-width="140">
					<template #default="{ row }">{{ row.orgName || '-' }}</template>
				</el-table-column>
				<el-table-column label="启用" width="80" align="center">
					<template #default="{ row }">
						<el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
					</template>
				</el-table-column>
				<el-table-column label="在线" width="80" align="center">
					<template #default="{ row }">
						<el-tag v-if="row.online === true" type="success" size="small">在线</el-tag>
						<el-tag v-else-if="row.online === false" type="danger" size="small">离线</el-tag>
						<el-tag v-else type="info" size="small">未知</el-tag>
					</template>
				</el-table-column>
				<el-table-column prop="version" label="版本" width="110">
					<template #default="{ row }">{{ row.version || '-' }}</template>
				</el-table-column>
				<el-table-column label="最后心跳" width="170">
					<template #default="{ row }">{{ fmt(row.lastHeartbeatTime) }}</template>
				</el-table-column>
				<el-table-column label="最后上报" width="170">
					<template #default="{ row }">{{ fmt(row.lastReportTime) }}</template>
				</el-table-column>
				<el-table-column prop="remark" label="备注" min-width="160" show-overflow-tooltip />
				<el-table-column label="操作" width="150" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="handleDelete(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>

			<div class="pagination">
				<el-pagination
					v-model:current-page="page"
					v-model:page-size="pageSize"
					:total="total"
					:page-sizes="[10, 20, 50]"
					layout="total, sizes, prev, pager, next, jumper"
					@size-change="loadList"
					@current-change="loadList"
				/>
			</div>
		</el-card>

		<el-drawer v-model="drawerVisible" :title="isEdit ? '编辑边缘节点' : '新增边缘节点'" size="480px">
			<el-form :model="form" label-width="100px" style="max-width: 420px">
				<el-form-item label="节点名称" required>
					<el-input v-model="form.name" placeholder="如：一号厂房边缘网关" maxlength="128" show-word-limit />
				</el-form-item>
				<el-form-item label="节点编号" required>
					<el-input v-model="form.nodeCode" placeholder="唯一编号，用于接入寻址" maxlength="128" show-word-limit />
				</el-form-item>
				<el-form-item label="所属组织">
					<el-select v-model="form.orgId" placeholder="不选则按当前用户组织" clearable style="width: 100%">
						<el-option v-for="o in orgOptions" :key="o.id" :label="o.name" :value="o.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="启用">
					<el-switch v-model="form.enabled" />
				</el-form-item>
				<el-form-item label="在线">
					<el-switch v-model="form.online" />
				</el-form-item>
				<el-form-item label="版本">
					<el-input v-model="form.version" placeholder="如 v1.0.0" maxlength="64" />
				</el-form-item>
				<el-form-item label="备注">
					<el-input v-model="form.remark" type="textarea" :rows="3" maxlength="512" show-word-limit />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :loading="saving" @click="submit">保存</el-button>
					<el-button @click="drawerVisible = false">取消</el-button>
				</el-form-item>
			</el-form>
		</el-drawer>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import { iotEdgeApi } from '/@/api/iot';
import request from '/@/utils/request';

const loading = ref(false);
const saving = ref(false);
const list = ref([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(10);
const orgOptions = ref([]);
const drawerVisible = ref(false);
const isEdit = ref(false);

const query = reactive({ keyword: '', online: null });

const defaultForm = () => ({
	id: 0,
	name: '',
	nodeCode: '',
	orgId: undefined,
	enabled: true,
	online: null,
	version: '',
	remark: '',
});
const form = reactive(defaultForm());

const fmt = (t) => (t ? new Date(t).toLocaleString() : '-');

const loadList = async () => {
	loading.value = true;
	try {
		const res = await iotEdgeApi.page({ page: page.value, pageSize: pageSize.value, ...query });
		const result = res?.data?.result || {};
		list.value = result.items || [];
		total.value = result.total || 0;
	} finally {
		loading.value = false;
	}
};

const loadOrgs = async () => {
	try {
		const res = await request({ url: '/api/sysOrg/list', method: 'get' });
		const data = res?.data?.result;
		orgOptions.value = Array.isArray(data) ? data : data?.items || [];
	} catch {
		orgOptions.value = [];
	}
};

const onSearch = () => { page.value = 1; loadList(); };
const onReset = () => { query.keyword = ''; query.online = null; onSearch(); };

const handleAdd = () => {
	Object.assign(form, defaultForm());
	isEdit.value = false;
	drawerVisible.value = true;
};

const handleEdit = (row) => {
	Object.assign(form, defaultForm(), row);
	isEdit.value = true;
	drawerVisible.value = true;
};

const submit = async () => {
	if (!form.name || !form.nodeCode) {
		ElMessage.warning('请填写节点名称和编号');
		return;
	}
	saving.value = true;
	try {
		const payload = { ...form };
		if (isEdit.value) await iotEdgeApi.update(payload);
		else await iotEdgeApi.add(payload);
		ElMessage.success('保存成功');
		drawerVisible.value = false;
		await loadList();
	} finally {
		saving.value = false;
	}
};

const handleDelete = (row) => {
	ElMessageBox.confirm(`确认删除边缘节点「${row.name}」？`, '提示', { type: 'warning' })
		.then(async () => {
			await iotEdgeApi.batchDelete([row.id]);
			ElMessage.success('删除成功');
			loadList();
		})
		.catch(() => {});
};

onMounted(() => { loadOrgs(); loadList(); });
</script>

<style scoped>
.iot-page { padding: 16px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.pagination { margin-top: 12px; display: flex; justify-content: flex-end; }
</style>
