<template>
	<div class="iot-page">
		<el-card shadow="never" v-loading="loading">
			<template #header>
				<span>系统传输配置</span>
			</template>
			<el-form :model="form" label-width="140px" style="max-width: 720px">
				<el-form-item label="网关名称">
					<el-input v-model="form.gatewayName" />
				</el-form-item>
				<el-form-item label="ClientId">
					<el-input v-model="form.clientId" />
				</el-form-item>
				<el-form-item label="MQTT 服务地址">
					<el-input v-model="form.mqttIp" placeholder="如 127.0.0.1" />
				</el-form-item>
				<el-form-item label="MQTT 端口">
					<el-input v-model.number="form.mqttPort" type="number" />
				</el-form-item>
				<el-form-item label="MQTT 用户名">
					<el-input v-model="form.mqttUName" />
				</el-form-item>
				<el-form-item label="MQTT 密码">
					<el-input v-model="form.mqttUPwd" type="password" show-password />
				</el-form-item>
				<el-form-item label="输出平台">
					<el-select v-model="form.iotPlatformType" style="width: 100%">
						<el-option v-for="item in platformOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :loading="saving" @click="save">保存配置</el-button>
					<el-button @click="loadData">重新加载</el-button>
				</el-form-item>
			</el-form>
		</el-card>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage } from 'element-plus';
import { iotSystemConfigApi } from '/@/api/iot';

const loading = ref(false);
const saving = ref(false);
const form = reactive({
	id: 0,
	gatewayName: '',
	clientId: '',
	mqttIp: '',
	mqttPort: 1888,
	mqttUName: '',
	mqttUPwd: '',
	iotPlatformType: 8,
});

const platformOptions = [
	{ label: 'ThingsBoard', value: 0 },
	{ label: 'IoTSharp', value: 1 },
	{ label: '阿里云IoT', value: 2 },
	{ label: '腾讯IoTHub', value: 3 },
	{ label: '百度IoTCore', value: 4 },
	{ label: 'OneNet', value: 5 },
	{ label: 'ThingsCloud', value: 6 },
	{ label: '华为云', value: 7 },
	{ label: 'IoTGateway', value: 8 },
	{ label: 'ThingsPanel', value: 9 },
];

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotSystemConfigApi.get();
		Object.assign(form, res.data.result || {});
	} finally {
		loading.value = false;
	}
};

const save = async () => {
	saving.value = true;
	try {
		await iotSystemConfigApi.save({ ...form });
		ElMessage.success('保存成功');
		await loadData();
	} finally {
		saving.value = false;
	}
};

onMounted(loadData);
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
</style>
