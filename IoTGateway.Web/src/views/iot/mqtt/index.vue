<template>
	<div class="iot-page">
		<el-card shadow="never" v-loading="loading">
			<template #header>
				<div class="card-header">
					<span>MQTT Broker 运行状态</span>
					<el-button type="primary" link @click="loadData">刷新</el-button>
				</div>
			</template>

			<el-descriptions :column="4" border>
				<el-descriptions-item label="运行状态">
					<el-tag :type="status.running ? 'success' : 'danger'">{{ status.running ? '运行中' : '未运行' }}</el-tag>
				</el-descriptions-item>
				<el-descriptions-item label="监听端口">{{ status.port }}</el-descriptions-item>
				<el-descriptions-item label="服务地址">{{ status.server }}</el-descriptions-item>
				<el-descriptions-item label="网关名称">{{ status.gatewayName }}</el-descriptions-item>
			</el-descriptions>

			<el-divider content-position="left">内部客户端订阅</el-divider>
			<el-table :data="status.clients || []" border size="small" empty-text="暂无内部客户端连接">
				<el-table-column prop="clientId" label="ClientId" min-width="220" />
				<el-table-column label="订阅主题" min-width="320">
					<template #default="{ row }">
						<el-tag v-for="topic in row.topics" :key="topic" class="topic-tag" type="info">{{ topic }}</el-tag>
						<span v-if="!row.topics || row.topics.length === 0">-</span>
					</template>
				</el-table-column>
			</el-table>
		</el-card>

		<el-card shadow="never" class="publish-card">
			<template #header>
				<div class="card-header">
					<span>WebSocket 实时订阅（/mqtt）</span>
					<el-tag :type="wsConnected ? 'success' : 'info'">{{ wsConnected ? '已连接' : '未连接' }}</el-tag>
				</div>
			</template>
			<el-form :inline="true" :model="wsForm">
				<el-form-item label="地址">
					<el-input v-model="wsForm.url" style="width: 340px" />
				</el-form-item>
				<el-form-item label="订阅主题">
					<el-input v-model="wsForm.topics" placeholder="internal/v1/gateway/#" style="width: 280px" />
				</el-form-item>
				<el-form-item>
					<el-button v-if="!wsConnected" type="primary" :loading="wsConnecting" @click="connectWs">连接</el-button>
					<el-button v-else type="danger" @click="disconnectWs">断开</el-button>
					<el-button @click="messages = []">清空消息</el-button>
				</el-form-item>
			</el-form>
			<el-table :data="messages" border size="small" max-height="320" empty-text="暂无消息">
				<el-table-column prop="time" label="时间" width="170" />
				<el-table-column prop="topic" label="主题" min-width="300" />
				<el-table-column prop="payload" label="内容" min-width="320" show-overflow-tooltip />
			</el-table>
		</el-card>

		<el-card shadow="never" class="publish-card">
			<template #header>消息发布（联调）</template>
			<el-form :inline="true" :model="publishForm">
				<el-form-item label="主题">
					<el-input v-model="publishForm.topic" placeholder="如 devices/device1/telemetry" style="width: 300px" />
				</el-form-item>
				<el-form-item label="内容">
					<el-input v-model="publishForm.payload" placeholder='{"temperature":25.6}' style="width: 320px" />
				</el-form-item>
				<el-form-item label="保留">
					<el-switch v-model="publishForm.retain" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :loading="publishing" @click="publish">发布</el-button>
				</el-form-item>
			</el-form>
		</el-card>
	</div>
</template>

<script setup>
import { onMounted, onUnmounted, reactive, ref } from 'vue';
import { ElMessage } from 'element-plus';
import mqtt from 'mqtt';
import { iotMqttApi } from '/@/api/iot';

const loading = ref(false);
const publishing = ref(false);
const status = ref({ running: false, port: 1888, server: '', gatewayName: '', clients: [] });
const publishForm = reactive({ topic: '', payload: '', retain: false });

const wsProtocol = window.location.protocol === 'https:' ? 'wss' : 'ws';
const wsForm = reactive({ url: `${wsProtocol}://${window.location.host}/mqtt`, topics: 'internal/v1/gateway/#' });
const wsConnected = ref(false);
const wsConnecting = ref(false);
const messages = ref([]);
let wsClient = null;

const connectWs = () => {
	wsConnecting.value = true;
	const clientId = `iotgatewayInternal_web_${Math.random().toString(16).slice(2, 10)}`;
	wsClient = mqtt.connect(wsForm.url, { clientId, connectTimeout: 8000, reconnectPeriod: 0, clean: true });
	wsClient.on('connect', () => {
		wsConnected.value = true;
		wsConnecting.value = false;
		const topics = (wsForm.topics || '')
			.split(',')
			.map((x) => x.trim())
			.filter(Boolean);
		topics.forEach((topic) => wsClient.subscribe(topic, { qos: 0 }));
		ElMessage.success('MQTT WebSocket 已连接');
	});
	wsClient.on('message', (topic, payload) => {
		messages.value.unshift({
			time: new Date().toLocaleString('zh-CN', { hour12: false }),
			topic,
			payload: payload.toString().slice(0, 500),
		});
		if (messages.value.length > 100) messages.value.pop();
	});
	wsClient.on('error', (err) => {
		wsConnecting.value = false;
		wsConnected.value = false;
		ElMessage.error(`MQTT 连接失败：${err.message}`);
	});
	wsClient.on('close', () => {
		wsConnected.value = false;
	});
};

const disconnectWs = () => {
	if (wsClient) {
		wsClient.end(true);
		wsClient = null;
	}
	wsConnected.value = false;
};

onUnmounted(disconnectWs);

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotMqttApi.status();
		status.value = res.data.result || {};
	} finally {
		loading.value = false;
	}
};

const publish = async () => {
	if (!publishForm.topic) {
		ElMessage.warning('请输入主题');
		return;
	}
	publishing.value = true;
	try {
		await iotMqttApi.publish({ ...publishForm });
		ElMessage.success('发布成功');
	} finally {
		publishing.value = false;
	}
};

onMounted(loadData);
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.card-header {
	display: flex;
	justify-content: space-between;
	align-items: center;
}
.publish-card {
	margin-top: 16px;
}
.topic-tag {
	margin: 2px 4px 2px 0;
}
</style>
