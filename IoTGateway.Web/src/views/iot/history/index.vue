<template>
	<div class="iot-page" v-loading="loading">
		<el-card shadow="never" class="filter-card">
			<el-form :inline="true" @submit.prevent>
				<el-form-item label="设备">
					<el-select v-model="deviceId" placeholder="选择设备" filterable style="width: 220px" @change="onDeviceChange">
						<el-option v-for="d in devices" :key="d.id" :label="d.deviceName" :value="d.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="变量">
					<el-select
						v-model="variableIds"
						multiple
						collapse-tags
						collapse-tags-tooltip
						filterable
						placeholder="选择变量（可多选）"
						style="width: 320px"
						:disabled="!deviceId"
					>
						<el-option v-for="v in variables" :key="v.id" :label="v.name" :value="v.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="时间范围">
					<el-radio-group v-model="rangeKey" @change="applyQuickRange">
						<el-radio-button label="1h">1小时</el-radio-button>
						<el-radio-button label="6h">6小时</el-radio-button>
						<el-radio-button label="24h">24小时</el-radio-button>
						<el-radio-button label="7d">7天</el-radio-button>
						<el-radio-button label="custom">自定义</el-radio-button>
					</el-radio-group>
					<el-date-picker
						v-model="timeRange"
						type="datetimerange"
						range-separator="至"
						start-placeholder="开始"
						end-placeholder="结束"
						value-format="YYYY-MM-DD HH:mm:ss"
						style="margin-left: 8px; width: 340px"
						@change="rangeKey = 'custom'"
					/>
				</el-form-item>
				<el-form-item label="聚合">
					<el-select v-model="aggregate" style="width: 110px">
						<el-option label="原始 raw" value="raw" />
						<el-option label="平均 avg" value="avg" />
						<el-option label="最大 max" value="max" />
						<el-option label="最小 min" value="min" />
						<el-option label="求和 sum" value="sum" />
						<el-option label="计数 count" value="count" />
					</el-select>
				</el-form-item>
				<el-form-item label="桶(秒)" v-if="aggregate !== 'raw'">
					<el-input-number v-model="bucketSeconds" :min="1" :max="86400" style="width: 120px" />
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :disabled="!canQuery" @click="loadData">查询</el-button>
					<el-button @click="loadDevices">刷新设备</el-button>
				</el-form-item>
			</el-form>
		</el-card>

		<el-card shadow="never" class="chart-card">
			<template #header>
				<div class="chart-header">
					<span>变量趋势（{{ points.length }} 个数据点）</span>
					<span class="hint" v-if="!points.length && queried">暂无数据，请确认设备已采集或调整时间范围</span>
				</div>
			</template>
			<scEcharts height="460px" :option="chartOption" />
		</el-card>

		<el-card shadow="never" class="chart-card" v-if="summaryRows.length">
			<template #header><span>变量统计</span></template>
			<el-table :data="summaryRows" size="small" stripe>
				<el-table-column prop="name" label="变量" min-width="160" />
				<el-table-column prop="count" label="点数" width="100" />
				<el-table-column prop="min" label="最小" width="120" />
				<el-table-column prop="max" label="最大" width="120" />
				<el-table-column prop="avg" label="平均" width="120" />
				<el-table-column prop="last" label="最新值" width="120" />
				<el-table-column prop="lastTime" label="最新时间" min-width="160" />
			</el-table>
		</el-card>
	</div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue';
import scEcharts from '/@/components/scEcharts/index.vue';
import { iotDeviceApi, iotDeviceVariableApi, iotHistoryApi } from '/@/api/iot';

const COLORS = ['#5470c6', '#91cc75', '#fac858', '#ee6666', '#73c0de', '#3ba272', '#fc8452', '#9a60b4', '#ea7ccc', '#48b8d0'];

const loading = ref(false);
const devices = ref([]);
const variables = ref([]);
const deviceId = ref(null);
const variableIds = ref([]);
const timeRange = ref([]);
const rangeKey = ref('1h');
const aggregate = ref('raw');
const bucketSeconds = ref(60);
const points = ref([]);
const queried = ref(false);

const canQuery = computed(() => !!deviceId.value && variableIds.value.length > 0);

const varNameMap = computed(() => {
	const m = {};
	variables.value.forEach((v) => (m[v.id] = v.name));
	return m;
});

// 按变量分组，返回 ECharts series
const seriesList = computed(() => {
	const groups = {};
	points.value.forEach((p) => {
		if (!groups[p.variableId]) groups[p.variableId] = [];
		groups[p.variableId].push(p);
	});
	return Object.keys(groups).map((vid, idx) => {
		const arr = groups[vid]
			.filter((p) => p.value !== null && p.value !== undefined)
			.map((p) => [toTs(p.time), p.value])
			.sort((a, b) => a[0] - b[0]);
		return {
			name: varNameMap.value[vid] || `变量${vid}`,
			type: 'line',
			showSymbol: false,
			smooth: true,
			itemStyle: { color: COLORS[idx % COLORS.length] },
			data: arr,
		};
	});
});

const chartOption = computed(() => ({
	tooltip: { trigger: 'axis' },
	legend: { top: 0, type: 'scroll' },
	grid: { left: 16, right: 24, top: 40, bottom: 40, containLabel: true },
	xAxis: { type: 'time' },
	yAxis: { type: 'value', scale: true },
	dataZoom: [{ type: 'inside' }, { type: 'slider', height: 16, bottom: 8 }],
	series: seriesList.value,
}));

const summaryRows = computed(() => {
	const groups = {};
	points.value.forEach((p) => {
		if (!groups[p.variableId]) groups[p.variableId] = [];
		groups[p.variableId].push(p);
	});
	return Object.keys(groups).map((vid) => {
		const arr = groups[vid].filter((p) => p.value !== null && p.value !== undefined);
		const vals = arr.map((p) => p.value);
		const last = arr.sort((a, b) => toTs(a.time) - toTs(b.time)).slice(-1)[0];
		return {
			name: varNameMap.value[vid] || `变量${vid}`,
			count: arr.length,
			min: vals.length ? round(Math.min(...vals)) : '-',
			max: vals.length ? round(Math.max(...vals)) : '-',
			avg: vals.length ? round(vals.reduce((s, v) => s + v, 0) / vals.length) : '-',
			last: last ? round(last.value) : '-',
			lastTime: last ? formatTime(last.time) : '-',
		};
	});
});

function round(n) {
	return Math.round(n * 1000) / 1000;
}

function toTs(t) {
	if (typeof t === 'number') return t;
	if (t instanceof Date) return t.getTime();
	// 兼容 "yyyy-MM-dd HH:mm:ss" 与 ISO 8601
	return new Date(String(t).replace(' ', 'T')).getTime();
}

// 本地时间字符串/Date -> ISO 8601 UTC（带 Z），保证跨时区一致
function toIso(t) {
	if (!t) return t;
	if (t instanceof Date) return t.toISOString();
	const d = new Date(String(t).replace(' ', 'T'));
	return isNaN(d.getTime()) ? t : d.toISOString();
}

// ISO/时间戳 -> 本地 "yyyy-MM-dd HH:mm:ss" 展示
function formatTime(t) {
	if (!t) return '-';
	const d = t instanceof Date ? t : new Date(t);
	if (isNaN(d.getTime())) return String(t);
	const p = (n) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function applyQuickRange(key) {
	const end = new Date();
	const start = new Date();
	if (key === '1h') start.setHours(start.getHours() - 1);
	else if (key === '6h') start.setHours(start.getHours() - 6);
	else if (key === '24h') start.setHours(start.getHours() - 24);
	else if (key === '7d') start.setDate(start.getDate() - 7);
	else return;
	const fmt = (d) => {
		const p = (n) => String(n).padStart(2, '0');
		return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
	};
	timeRange.value = [fmt(start), fmt(end)];
}

async function loadDevices() {
	const res = await iotDeviceApi.list();
	devices.value = res?.data?.result || [];
}

async function onDeviceChange() {
	variableIds.value = [];
	variables.value = [];
	if (!deviceId.value) return;
	const res = await iotDeviceVariableApi.list(deviceId.value);
	variables.value = res?.data?.result || [];
}

async function loadData() {
	if (!canQuery.value) return;
	loading.value = true;
	try {
		const payload = {
			deviceId: deviceId.value,
			variableIds: variableIds.value,
			aggregate: aggregate.value,
		};
		if (timeRange.value && timeRange.value.length === 2) {
			// 统一发送 ISO 8601 UTC，避免浏览器与服务器时区不一致导致查不到数据
			payload.startTime = toIso(timeRange.value[0]);
			payload.endTime = toIso(timeRange.value[1]);
		}
		if (aggregate.value !== 'raw') payload.bucketSeconds = bucketSeconds.value;
		payload.limit = 20000;
		const res = await iotHistoryApi.query(payload);
		points.value = res?.data?.result || [];
		queried.value = true;
	} finally {
		loading.value = false;
	}
}

onMounted(() => {
	applyQuickRange('1h');
	loadDevices();
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.filter-card {
	margin-bottom: 12px;
}
.chart-card {
	margin-bottom: 12px;
}
.chart-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
}
.hint {
	color: var(--el-text-color-secondary);
	font-size: 13px;
}
</style>
