<template>
	<div class="iot-page" v-loading="loading">
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
				<el-card shadow="never"><div class="stat-label">采集变量</div><div class="stat-value">{{ stats.variableCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">通讯配置</div><div class="stat-value">{{ stats.configCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">驱动数量</div><div class="stat-value">{{ stats.driverCount || 0 }}</div></el-card>
			</el-col>
		</el-row>

		<el-row :gutter="12">
			<el-col :xs="24" :md="10">
				<el-card shadow="never" class="chart-card">
					<template #header>
						<div class="chart-header">
							<span>设备状态</span>
							<el-button link type="primary" @click="loadData">刷新</el-button>
						</div>
					</template>
					<scEcharts height="320px" :option="deviceOption"></scEcharts>
				</el-card>
			</el-col>
			<el-col :xs="24" :md="14">
				<el-card shadow="never" class="chart-card">
					<template #header>
						<div class="chart-header">
							<span>设备变量状态</span>
							<el-button link type="primary" @click="loadData">刷新</el-button>
						</div>
					</template>
					<scEcharts height="320px" :option="variableOption"></scEcharts>
				</el-card>
			</el-col>
		</el-row>
	</div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import scEcharts from '/@/components/scEcharts/index.vue';
import { iotDashboardApi, iotDeviceApi } from '/@/api/iot';

const loading = ref(false);
const stats = ref({});
const deviceChart = ref([]);
const variableChart = ref([]);
let timer = null;

const deviceOption = computed(() => ({
	tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
	legend: { bottom: 0 },
	series: [
		{
			name: '设备状态',
			type: 'pie',
			radius: ['45%', '70%'],
			center: ['50%', '45%'],
			label: { formatter: '{b}\n{c}' },
			data: (deviceChart.value || []).map((x) => ({
				name: x.category,
				value: x.value,
				itemStyle: { color: x.category === '运行' ? '#67C23A' : x.category === '异常' ? '#F56C6C' : '#909399' },
			})),
		},
	],
}));

const variableOption = computed(() => {
	const rows = variableChart.value || [];
	return {
		tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
		legend: { bottom: 0 },
		grid: { left: 16, right: 24, top: 24, bottom: 48, containLabel: true },
		xAxis: { type: 'category', data: rows.map((x) => x.deviceName), axisLabel: { interval: 0, rotate: rows.length > 6 ? 30 : 0 } },
		yAxis: { type: 'value', name: '变量数' },
		series: [
			{ name: '正常', type: 'bar', stack: 'total', color: '#67C23A', data: rows.map((x) => x.goodCount) },
			{ name: '异常', type: 'bar', stack: 'total', color: '#F56C6C', data: rows.map((x) => x.badCount) },
		],
	};
});

const loadData = async () => {
	loading.value = true;
	try {
		const [statsRes, deviceRes, variableRes] = await Promise.all([
			iotDeviceApi.stats(),
			iotDashboardApi.deviceStatus(),
			iotDashboardApi.variableStatus(),
		]);
		stats.value = statsRes.data.result || {};
		deviceChart.value = deviceRes.data.result || [];
		variableChart.value = variableRes.data.result || [];
	} finally {
		loading.value = false;
	}
};

onMounted(() => {
	loadData();
	timer = setInterval(loadData, 10000);
});

onBeforeUnmount(() => {
	if (timer) clearInterval(timer);
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
.chart-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
}
.chart-card {
	margin-bottom: 12px;
}
</style>
