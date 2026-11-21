<template>
	<div class="iot-page">
		<el-card shadow="never">
			<el-form :inline="true" :model="query" class="iot-search">
				<el-form-item label="设备名称">
					<el-input v-model="query.keyword" placeholder="请输入设备名称" clearable @keyup.enter="loadData" />
				</el-form-item>
				<el-form-item label="驱动">
					<el-select v-model="query.driverId" placeholder="全部" clearable style="width: 180px">
						<el-option v-for="item in drivers" :key="item.id" :label="item.driverName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="loadData">查询</el-button>
					<el-button @click="resetQuery">重置</el-button>
					<el-button type="success" @click="openAdd">新增设备</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe>
				<el-table-column prop="deviceName" label="设备名称" min-width="150" />
				<el-table-column prop="index" label="排序" width="80" />
				<el-table-column prop="driverName" label="驱动" min-width="130" />
				<el-table-column label="类型" width="90">
					<template #default="{ row }">
						<el-tag :type="row.deviceTypeEnum === 0 ? 'info' : 'success'">{{ row.deviceTypeEnum === 0 ? '分组' : '设备' }}</el-tag>
					</template>
				</el-table-column>
				<el-table-column label="启动" width="80">
					<template #default="{ row }">{{ row.autoStart ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column label="变化上传" width="100">
					<template #default="{ row }">{{ row.cgUpload ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column prop="enforcePeriod" label="归档周期(ms)" width="130" />
				<el-table-column prop="cmdPeriod" label="指令间隔(ms)" width="130" />
				<el-table-column prop="description" label="描述" min-width="140" show-overflow-tooltip />
				<el-table-column label="操作" width="200" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openEdit(row)">编辑</el-button>
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
				:page-sizes="[10, 20, 50, 100]"
				layout="total, sizes, prev, pager, next, jumper"
				@current-change="loadData"
				@size-change="loadData"
			/>
		</el-card>

		<el-dialog v-model="dialogVisible" :title="form.id ? '编辑设备' : '新增设备'" width="620px" destroy-on-close>
			<el-form :model="form" label-width="110px">
				<el-form-item label="设备名称" required>
					<el-input v-model="form.deviceName" placeholder="请输入设备名称" />
				</el-form-item>
				<el-form-item label="类型">
					<el-select v-model="form.deviceTypeEnum" style="width: 100%">
						<el-option label="设备" :value="1" />
						<el-option label="分组" :value="0" />
					</el-select>
				</el-form-item>
				<el-form-item label="驱动">
					<el-select v-model="form.driverId" placeholder="请选择驱动" clearable style="width: 100%">
						<el-option v-for="item in drivers" :key="item.id" :label="item.driverName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="排序">
					<el-input v-model.number="form.index" type="number" />
				</el-form-item>
				<el-form-item label="归档周期(ms)">
					<el-input v-model.number="form.enforcePeriod" type="number" />
				</el-form-item>
				<el-form-item label="指令间隔(ms)">
					<el-input v-model.number="form.cmdPeriod" type="number" />
				</el-form-item>
				<el-form-item label="自动启动">
					<el-switch v-model="form.autoStart" />
				</el-form-item>
				<el-form-item label="变化上传">
					<el-switch v-model="form.cgUpload" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="form.description" type="textarea" :rows="3" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="dialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="saving" @click="save">保存</el-button>
			</template>
		</el-dialog>

		<el-drawer v-model="detailVisible" title="设备详情" size="55%">
			<el-descriptions :column="2" border>
				<el-descriptions-item label="设备名称">{{ detail.deviceName }}</el-descriptions-item>
				<el-descriptions-item label="驱动">{{ detail.driverName }}</el-descriptions-item>
				<el-descriptions-item label="类型">{{ detail.deviceTypeEnum === 0 ? '分组' : '设备' }}</el-descriptions-item>
				<el-descriptions-item label="排序">{{ detail.index }}</el-descriptions-item>
				<el-descriptions-item label="描述" :span="2">{{ detail.description }}</el-descriptions-item>
			</el-descriptions>

			<el-divider content-position="left">通讯配置</el-divider>
			<el-table :data="detail.deviceConfigs || []" border size="small">
				<el-table-column prop="deviceConfigName" label="名称" min-width="120" />
				<el-table-column prop="dataSide" label="属性侧" width="100" />
				<el-table-column prop="value" label="值" min-width="150" />
				<el-table-column prop="description" label="描述" min-width="140" />
			</el-table>

			<el-divider content-position="left">变量</el-divider>
			<el-table :data="detail.deviceVariables || []" border size="small">
				<el-table-column prop="name" label="变量名" min-width="120" />
				<el-table-column prop="deviceAddress" label="地址" min-width="110" />
				<el-table-column prop="dataType" label="数据类型" width="100" />
				<el-table-column prop="protectType" label="权限" width="90" />
				<el-table-column prop="isUpload" label="上传" width="80">
					<template #default="{ row }">{{ row.isUpload ? '是' : '否' }}</template>
				</el-table-column>
			</el-table>
		</el-drawer>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotDeviceApi } from '/@/api/iot';

const loading = ref(false);
const saving = ref(false);
const tableData = ref([]);
const drivers = ref([]);
const total = ref(0);
const dialogVisible = ref(false);
const detailVisible = ref(false);
const detail = ref({});

const query = reactive({ keyword: '', driverId: null, page: 1, pageSize: 20 });
const form = reactive({
	id: 0,
	deviceName: '',
	deviceTypeEnum: 1,
	driverId: null,
	index: 0,
	enforcePeriod: 0,
	cmdPeriod: 1000,
	autoStart: true,
	cgUpload: false,
	description: '',
	parentId: null,
});

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotDeviceApi.page({ ...query });
		tableData.value = res.data.result.items || [];
		total.value = res.data.result.total || 0;
	} finally {
		loading.value = false;
	}
};

const loadDrivers = async () => {
	const res = await iotDeviceApi.driverList();
	drivers.value = res.data.result || [];
};

const resetQuery = () => {
	query.keyword = '';
	query.driverId = null;
	query.page = 1;
	loadData();
};

const resetForm = () => {
	Object.assign(form, {
		id: 0,
		deviceName: '',
		deviceTypeEnum: 1,
		driverId: null,
		index: 0,
		enforcePeriod: 0,
		cmdPeriod: 1000,
		autoStart: true,
		cgUpload: false,
		description: '',
		parentId: null,
	});
};

const openAdd = () => {
	resetForm();
	dialogVisible.value = true;
};

const openEdit = (row) => {
	Object.assign(form, {
		id: row.id,
		deviceName: row.deviceName,
		deviceTypeEnum: row.deviceTypeEnum,
		driverId: row.driverId,
		index: row.index,
		enforcePeriod: row.enforcePeriod,
		cmdPeriod: row.cmdPeriod,
		autoStart: row.autoStart,
		cgUpload: row.cgUpload,
		description: row.description,
		parentId: row.parentId,
	});
	dialogVisible.value = true;
};

const save = async () => {
	if (!form.deviceName) {
		ElMessage.warning('请输入设备名称');
		return;
	}
	saving.value = true;
	try {
		if (form.id) await iotDeviceApi.update({ ...form });
		else await iotDeviceApi.add({ ...form });
		ElMessage.success('保存成功');
		dialogVisible.value = false;
		await loadData();
	} finally {
		saving.value = false;
	}
};

const remove = async (row) => {
	await ElMessageBox.confirm(`确认删除设备「${row.deviceName}」吗？`, '提示', { type: 'warning' });
	await iotDeviceApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
};

const openDetail = async (row) => {
	const res = await iotDeviceApi.detail(row.id);
	detail.value = res.data.result || {};
	detailVisible.value = true;
};

onMounted(async () => {
	await loadDrivers();
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
</style>
