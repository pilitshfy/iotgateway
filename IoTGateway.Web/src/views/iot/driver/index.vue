<template>
	<div class="iot-page">
		<el-card shadow="never">
			<el-form :inline="true" :model="query" class="iot-search">
				<el-form-item label="驱动名称">
					<el-input v-model="query.keyword" placeholder="请输入驱动名称" clearable @keyup.enter="loadData" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="loadData">查询</el-button>
					<el-button type="success" @click="openAdd">新增驱动</el-button>
					<el-button @click="reloadDrivers">重新扫描驱动</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe>
				<el-table-column prop="driverName" label="驱动名称" min-width="160" />
				<el-table-column prop="fileName" label="文件名" min-width="220" />
				<el-table-column prop="assembleName" label="程序集名" min-width="240" />
				<el-table-column prop="authorizesNum" label="剩余授权数" width="120" />
				<el-table-column label="操作" width="150" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openEdit(row)">编辑</el-button>
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

		<el-dialog v-model="dialogVisible" :title="form.id ? '编辑驱动' : '新增驱动'" width="560px" destroy-on-close>
			<el-form :model="form" label-width="100px">
				<el-form-item label="驱动文件">
					<el-select v-model="selectedFile" placeholder="从驱动目录选择，自动填充" clearable filterable style="width: 100%" @change="onFileChange">
						<el-option
							v-for="item in driverFiles"
							:key="item.fileName + item.assembleName"
							:label="`${item.driverName} (${item.fileName})`"
							:value="item.fileName + '|' + item.assembleName"
						/>
					</el-select>
				</el-form-item>
				<el-form-item label="驱动名称" required>
					<el-input v-model="form.driverName" />
				</el-form-item>
				<el-form-item label="文件名">
					<el-input v-model="form.fileName" placeholder="如 Modbus.dll" />
				</el-form-item>
				<el-form-item label="程序集名">
					<el-input v-model="form.assembleName" />
				</el-form-item>
				<el-form-item label="剩余授权数">
					<el-input v-model.number="form.authorizesNum" type="number" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="dialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="saving" @click="save">保存</el-button>
			</template>
		</el-dialog>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotDriverApi } from '/@/api/iot';

const loading = ref(false);
const saving = ref(false);
const tableData = ref([]);
const total = ref(0);
const dialogVisible = ref(false);
const driverFiles = ref([]);
const selectedFile = ref('');
const query = reactive({ keyword: '', page: 1, pageSize: 20 });
const form = reactive({ id: 0, driverName: '', fileName: '', assembleName: '', authorizesNum: 0 });

const loadDriverFiles = async () => {
	try {
		const res = await iotDriverApi.driverFiles();
		driverFiles.value = res.data.result || [];
	} catch {
		driverFiles.value = [];
	}
};

const onFileChange = (value) => {
	if (!value) return;
	const [fileName, assembleName] = value.split('|');
	const matched = driverFiles.value.find((x) => x.fileName === fileName && x.assembleName === assembleName);
	form.fileName = fileName;
	form.assembleName = assembleName;
	if (matched) form.driverName = matched.driverName;
};

const reloadDrivers = async () => {
	await iotDriverApi.reload();
	await loadDriverFiles();
	ElMessage.success('驱动目录已重新扫描');
};

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotDriverApi.page({ ...query });
		tableData.value = res.data.result.items || [];
		total.value = res.data.result.total || 0;
	} finally {
		loading.value = false;
	}
};

const openAdd = () => {
	Object.assign(form, { id: 0, driverName: '', fileName: '', assembleName: '', authorizesNum: 0 });
	selectedFile.value = '';
	dialogVisible.value = true;
};

const openEdit = (row) => {
	Object.assign(form, {
		id: row.id,
		driverName: row.driverName,
		fileName: row.fileName,
		assembleName: row.assembleName,
		authorizesNum: row.authorizesNum,
	});
	selectedFile.value = row.fileName && row.assembleName ? `${row.fileName}|${row.assembleName}` : '';
	dialogVisible.value = true;
};

const save = async () => {
	if (!form.driverName) {
		ElMessage.warning('请输入驱动名称');
		return;
	}
	saving.value = true;
	try {
		if (form.id) await iotDriverApi.update({ ...form });
		else await iotDriverApi.add({ ...form });
		ElMessage.success('保存成功');
		dialogVisible.value = false;
		await loadData();
	} finally {
		saving.value = false;
	}
};

const remove = async (row) => {
	await ElMessageBox.confirm(`确认删除驱动「${row.driverName}」吗？`, '提示', { type: 'warning' });
	await iotDriverApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
};

onMounted(() => {
	loadData();
	loadDriverFiles();
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
