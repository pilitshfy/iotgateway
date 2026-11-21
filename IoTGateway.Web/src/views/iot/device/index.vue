<template>
	<div class="iot-page">
		<el-row :gutter="12" class="iot-stats">
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">设备总数</div><div class="stat-value">{{ stats.deviceCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">在线设备</div><div class="stat-value online">{{ stats.onlineCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">离线设备</div><div class="stat-value offline">{{ stats.offlineCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">分组数量</div><div class="stat-value">{{ stats.groupCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">采集变量</div><div class="stat-value">{{ stats.variableCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">驱动数量</div><div class="stat-value">{{ stats.driverCount || 0 }}</div></el-card>
			</el-col>
		</el-row>

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
				<el-form-item label="父级分组">
					<el-select v-model="form.parentId" placeholder="顶级" clearable style="width: 100%">
						<el-option v-for="item in groups" :key="item.id" :label="item.deviceName" :value="item.id" />
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

		<el-drawer v-model="detailVisible" title="设备详情" size="60%">
			<el-descriptions :column="2" border>
				<el-descriptions-item label="设备名称">{{ detail.deviceName }}</el-descriptions-item>
				<el-descriptions-item label="驱动">{{ detail.driverName }}</el-descriptions-item>
				<el-descriptions-item label="类型">{{ detail.deviceTypeEnum === 0 ? '分组' : '设备' }}</el-descriptions-item>
				<el-descriptions-item label="排序">{{ detail.index }}</el-descriptions-item>
				<el-descriptions-item label="描述" :span="2">{{ detail.description }}</el-descriptions-item>
			</el-descriptions>

			<el-divider content-position="left">通讯配置</el-divider>
			<div class="iot-section-toolbar">
				<el-button type="primary" size="small" @click="openConfigAdd">新增配置</el-button>
			</div>
			<el-table :data="detail.deviceConfigs || []" border size="small">
				<el-table-column prop="deviceConfigName" label="名称" min-width="120" />
				<el-table-column label="属性侧" width="100">
					<template #default="{ row }">{{ dataSideLabel(row.dataSide) }}</template>
				</el-table-column>
				<el-table-column prop="value" label="值" min-width="150" />
				<el-table-column prop="description" label="描述" min-width="140" />
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openConfigEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="removeConfig(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>

			<el-divider content-position="left">变量</el-divider>
			<div class="iot-section-toolbar">
				<el-button type="primary" size="small" @click="openVariableAdd">新增变量</el-button>
			</div>
			<el-table :data="detail.deviceVariables || []" border size="small">
				<el-table-column prop="name" label="变量名" min-width="120" />
				<el-table-column prop="alias" label="别名" min-width="100" />
				<el-table-column prop="deviceAddress" label="地址" min-width="110" />
				<el-table-column label="数据类型" width="100">
					<template #default="{ row }">{{ dataTypeLabel(row.dataType) }}</template>
				</el-table-column>
				<el-table-column label="权限" width="90">
					<template #default="{ row }">{{ protectLabel(row.protectType) }}</template>
				</el-table-column>
				<el-table-column prop="method" label="方法" min-width="120" show-overflow-tooltip />
				<el-table-column label="上传" width="70">
					<template #default="{ row }">{{ row.isUpload ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openVariableEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="removeVariable(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>
		</el-drawer>

		<el-dialog v-model="configDialogVisible" :title="configForm.id ? '编辑通讯配置' : '新增通讯配置'" width="560px" destroy-on-close>
			<el-form :model="configForm" label-width="110px">
				<el-form-item label="名称" required>
					<el-select v-if="driverConfigs.length" v-model="configForm.deviceConfigName" filterable style="width: 100%" @change="onConfigNameChange">
						<el-option v-for="item in driverConfigs" :key="item.name" :label="`${item.name} (${item.description})`" :value="item.name" />
					</el-select>
					<el-input v-else v-model="configForm.deviceConfigName" placeholder="如 IpAddress" />
				</el-form-item>
				<el-form-item label="属性侧">
					<el-select v-model="configForm.dataSide" style="width: 100%">
						<el-option label="任意侧" :value="0" />
						<el-option label="客户端" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="值">
					<el-input v-model="configForm.value" placeholder="请输入配置值" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="configForm.description" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="configDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="configSaving" @click="saveConfig">保存</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="variableDialogVisible" :title="variableForm.id ? '编辑变量' : '新增变量'" width="620px" destroy-on-close>
			<el-form :model="variableForm" label-width="110px">
				<el-form-item label="变量名" required>
					<el-input v-model="variableForm.name" placeholder="如 temperature" />
				</el-form-item>
				<el-form-item label="别名">
					<el-input v-model="variableForm.alias" placeholder="分组上传别名，可空" />
				</el-form-item>
				<el-form-item label="方法" required>
					<el-select v-model="variableForm.method" filterable style="width: 100%" @change="onMethodChange">
						<el-option v-for="item in methodOptions" :key="item.name" :label="`${item.displayName || item.name} (${item.name})`" :value="item.name" />
					</el-select>
				</el-form-item>
				<el-form-item label="地址">
					<el-input v-model="variableForm.deviceAddress" placeholder="如 0 或 40001" />
				</el-form-item>
				<el-form-item label="数据类型">
					<el-select v-model="variableForm.dataType" style="width: 100%">
						<el-option v-for="item in dataTypeOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="大小端">
					<el-select v-model="variableForm.endianType" style="width: 100%">
						<el-option v-for="item in endianOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="权限">
					<el-select v-model="variableForm.protectType" style="width: 100%">
						<el-option label="只读" :value="0" />
						<el-option label="读写" :value="1" />
						<el-option label="只写" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="表达式">
					<el-input v-model="variableForm.expressions" placeholder="如 temperature*0.1" />
				</el-form-item>
				<el-form-item label="排序">
					<el-input v-model.number="variableForm.index" type="number" />
				</el-form-item>
				<el-form-item label="上传">
					<el-switch v-model="variableForm.isUpload" />
				</el-form-item>
				<el-form-item label="触发">
					<el-switch v-model="variableForm.isTrigger" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="variableForm.description" type="textarea" :rows="2" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="variableDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="variableSaving" @click="saveVariable">保存</el-button>
			</template>
		</el-dialog>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotDeviceApi, iotDeviceConfigApi, iotDeviceVariableApi, iotDriverApi } from '/@/api/iot';

const loading = ref(false);
const saving = ref(false);
const tableData = ref([]);
const drivers = ref([]);
const groups = ref([]);
const total = ref(0);
const stats = ref({});
const dialogVisible = ref(false);
const detailVisible = ref(false);
const detail = ref({});

const configDialogVisible = ref(false);
const configSaving = ref(false);
const driverConfigs = ref([]);
const configForm = reactive({ id: 0, deviceId: 0, deviceConfigName: '', dataSide: 0, value: '', description: '', enumInfo: '' });

const variableDialogVisible = ref(false);
const variableSaving = ref(false);
const methodOptions = ref([]);
const variableForm = reactive({
	id: 0,
	deviceId: 0,
	name: '',
	alias: '',
	method: '',
	deviceAddress: '',
	dataType: 9,
	endianType: 2,
	expressions: '',
	isUpload: true,
	isTrigger: false,
	protectType: 0,
	index: 0,
	description: '',
});

const dataTypeOptions = [
	{ value: 0, label: 'bit' }, { value: 1, label: 'bool' }, { value: 2, label: 'uint8' }, { value: 3, label: 'int8' },
	{ value: 4, label: 'uint16' }, { value: 5, label: 'int16' }, { value: 6, label: 'bcd16' }, { value: 7, label: 'uint32' },
	{ value: 8, label: 'int32' }, { value: 9, label: 'float' }, { value: 10, label: 'bcd32' }, { value: 11, label: 'uint64' },
	{ value: 12, label: 'int64' }, { value: 13, label: 'double' }, { value: 14, label: 'ascii' }, { value: 15, label: 'utf8' },
	{ value: 16, label: 'datetime' }, { value: 17, label: 'timestamp(ms)' }, { value: 18, label: 'timestamp(s)' }, { value: 19, label: 'Any' },
	{ value: 25, label: 'Gb2312' },
];
const endianOptions = [
	{ value: 0, label: 'None' }, { value: 1, label: 'BigEndian' }, { value: 2, label: 'LittleEndian' },
	{ value: 3, label: 'BigEndianSwap' }, { value: 4, label: 'LittleEndianSwap' },
];

const dataSideLabel = (v) => (Number(v) === 2 ? '客户端' : '任意侧');
const dataTypeLabel = (v) => dataTypeOptions.find((x) => x.value === Number(v))?.label ?? v;
const protectLabel = (v) => ['只读', '读写', '只写'][Number(v)] ?? v;

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

const loadStats = async () => {
	try {
		const res = await iotDeviceApi.stats();
		stats.value = res.data.result || {};
	} catch {
		stats.value = {};
	}
};

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

const loadGroups = async () => {
	const res = await iotDeviceApi.page({ keyword: '', page: 1, pageSize: 500 });
	groups.value = (res.data.result.items || []).filter((x) => x.deviceTypeEnum === 0);
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
		await loadGroups();
		await loadStats();
	} finally {
		saving.value = false;
	}
};

const remove = async (row) => {
	await ElMessageBox.confirm(`确认删除设备「${row.deviceName}」吗？`, '提示', { type: 'warning' });
	await iotDeviceApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
	await loadGroups();
	await loadStats();
};

const refreshDetail = async () => {
	if (!detail.value.id) return;
	const res = await iotDeviceApi.detail(detail.value.id);
	detail.value = res.data.result || {};
};

const loadDriverMeta = async () => {
	const driver = drivers.value.find((x) => x.id === detail.value.driverId);
	driverConfigs.value = [];
	methodOptions.value = [];
	if (!driver?.assembleName) return;

	try {
		const [configRes, methodRes] = await Promise.all([
			iotDriverApi.driverConfigs(driver.assembleName),
			iotDriverApi.driverMethods(driver.assembleName),
		]);
		driverConfigs.value = configRes.data.result || [];
		methodOptions.value = methodRes.data.result || [];
	} catch {
		driverConfigs.value = [];
		methodOptions.value = [];
	}
};

const openDetail = async (row) => {
	const res = await iotDeviceApi.detail(row.id);
	detail.value = res.data.result || {};
	detailVisible.value = true;
	await loadDriverMeta();
};

const openConfigAdd = () => {
	Object.assign(configForm, { id: 0, deviceId: detail.value.id, deviceConfigName: '', dataSide: 0, value: '', description: '', enumInfo: '' });
	configDialogVisible.value = true;
};

const openConfigEdit = (row) => {
	Object.assign(configForm, {
		id: row.id,
		deviceId: row.deviceId,
		deviceConfigName: row.deviceConfigName,
		dataSide: Number(row.dataSide),
		value: row.value,
		description: row.description,
		enumInfo: row.enumInfo,
	});
	configDialogVisible.value = true;
};

const onConfigNameChange = (name) => {
	const matched = driverConfigs.value.find((x) => x.name === name);
	if (matched && !configForm.description) configForm.description = matched.description;
};

const saveConfig = async () => {
	if (!configForm.deviceConfigName) {
		ElMessage.warning('请输入配置名称');
		return;
	}
	configSaving.value = true;
	try {
		if (configForm.id) await iotDeviceConfigApi.update({ ...configForm });
		else await iotDeviceConfigApi.add({ ...configForm });
		ElMessage.success('保存成功');
		configDialogVisible.value = false;
		await refreshDetail();
	} finally {
		configSaving.value = false;
	}
};

const removeConfig = async (row) => {
	await ElMessageBox.confirm(`确认删除配置「${row.deviceConfigName}」吗？`, '提示', { type: 'warning' });
	await iotDeviceConfigApi.delete(row.id);
	ElMessage.success('删除成功');
	await refreshDetail();
};

const openVariableAdd = () => {
	Object.assign(variableForm, {
		id: 0,
		deviceId: detail.value.id,
		name: '',
		alias: '',
		method: '',
		deviceAddress: '',
		dataType: 9,
		endianType: 2,
		expressions: '',
		isUpload: true,
		isTrigger: false,
		protectType: 0,
		index: 0,
		description: '',
	});
	variableDialogVisible.value = true;
};

const openVariableEdit = (row) => {
	Object.assign(variableForm, {
		id: row.id,
		deviceId: row.deviceId,
		name: row.name,
		alias: row.alias,
		method: row.method,
		deviceAddress: row.deviceAddress,
		dataType: Number(row.dataType),
		endianType: Number(row.endianType),
		expressions: row.expressions,
		isUpload: row.isUpload,
		isTrigger: row.isTrigger,
		protectType: Number(row.protectType),
		index: row.index,
		description: row.description,
	});
	variableDialogVisible.value = true;
};

const onMethodChange = (name) => {
	const matched = methodOptions.value.find((x) => x.name === name);
	if (matched) variableForm.protectType = matched.protectType;
};

const saveVariable = async () => {
	if (!variableForm.name) {
		ElMessage.warning('请输入变量名');
		return;
	}
	if (!variableForm.method) {
		ElMessage.warning('请选择方法');
		return;
	}
	variableSaving.value = true;
	try {
		if (variableForm.id) await iotDeviceVariableApi.update({ ...variableForm });
		else await iotDeviceVariableApi.add({ ...variableForm });
		ElMessage.success('保存成功');
		variableDialogVisible.value = false;
		await refreshDetail();
	} finally {
		variableSaving.value = false;
	}
};

const removeVariable = async (row) => {
	await ElMessageBox.confirm(`确认删除变量「${row.name}」吗？`, '提示', { type: 'warning' });
	await iotDeviceVariableApi.delete(row.id);
	ElMessage.success('删除成功');
	await refreshDetail();
};

onMounted(async () => {
	await loadDrivers();
	await loadGroups();
	await loadData();
	await loadStats();
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.iot-stats {
	margin-bottom: 12px;
}
.iot-stats .el-card {
	margin-bottom: 8px;
}
.stat-label {
	font-size: 13px;
	color: var(--el-text-color-secondary);
}
.stat-value {
	margin-top: 6px;
	font-size: 22px;
	font-weight: 600;
}
.stat-value.online {
	color: var(--el-color-success);
}
.stat-value.offline {
	color: var(--el-color-info);
}
.iot-search {
	margin-bottom: 8px;
}
.iot-pager {
	margin-top: 16px;
	justify-content: flex-end;
}
.iot-section-toolbar {
	margin-bottom: 8px;
	text-align: right;
}
</style>
