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
						<el-radio-button value="24h">24小时</el-radio-button>
						<el-radio-button value="7d">7天</el-radio-button>
						<el-radio-button value="30d">30天</el-radio-button>
						<el-radio-button value="custom">自定义</el-radio-button>
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
						<el-option label="平均 avg" value="avg" />
						<el-option label="求和 sum" value="sum" />
						<el-option label="最大 max" value="max" />
						<el-option label="最小 min" value="min" />
						<el-option label="计数 count" value="count" />
					</el-select>
				</el-form-item>
				<el-form-item label="粒度">
					<el-select v-model="granularity" style="width: 110px">
						<el-option label="分钟" value="minute" />
						<el-option label="小时" value="hour" />
						<el-option label="天" value="day" />
						<el-option label="月" value="month" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" :disabled="!canQuery" @click="loadReport">查询</el-button>
					<el-button :disabled="!rows.length" @click="exportCsv">导出 CSV</el-button>
				</el-form-item>
			</el-form>
		</el-card>

		<el-card shadow="never" class="chart-card" v-if="columns.length">
			<template #header>
				<div class="chart-header">
					<span>{{ result.deviceName }} · 报表（{{ rows.length }} 行 · {{ granularityLabel }} · {{ aggregateLabel }}）</span>
					<span class="hint" v-if="!rows.length">暂无数据，请调整时间范围或粒度</span>
				</div>
			</template>
			<scEcharts height="420px" :option="chartOption" />
		</el-card>

		<el-card shadow="never" class="chart-card" v-if="columns.length && rows.length">
			<template #header><span>报表数据</span></template>
			<el-table :data="rows" size="small" stripe border height="440" style="width: 100%">
				<el-table-column label="时间" width="180" fixed>
					<template #default="{ row }">{{ formatTime(row.time) }}</template>
				</el-table-column>
				<el-table-column
					v-for="col in columns"
					:key="col.variableId"
					:label="col.name"
					min-width="140"
					align="right"
				>
					<template #default="{ row }">{{ fmtVal(row.values[col.variableId]) }}</template>
				</el-table-column>
			</el-table>
		</el-card>
	</div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue';
import { ElMessage } from 'element-plus';
import scEcharts from '/@/components/scEcharts/index.vue';
import { iotDeviceApi, iotDeviceVariableApi, iotReportApi } from '/@/api/iot';

const COLORS = ['#5470c6', '#91cc75', '#fac858', '#ee6666', '#73c0de', '#3ba272', '#fc8452', '#9a60b4', '#ea7ccc', '#48b8d0'];

const loading = ref(false);
const devices = ref([]);
const variables = ref([]);
const deviceId = ref(null);
const variableIds = ref([]);
const timeRange = ref([]);
const rangeKey = ref('24h');
const aggregate = ref('avg');
const granularity = ref('hour');

const result = ref({});
const columns = computed(() => result.value.columns || []);
const rows = computed(() => result.value.rows || []);

const canQuery = computed(() => !!deviceId.value && variableIds.value.length > 0);

const granularityLabel = computed(() => ({ minute: '分钟', hour: '小时', day: '天', month: '月' }[granularity.value] || granularity.value));
const aggregateLabel = computed(() => ({ avg: '均值', sum: '求和', max: '最大', min: '最小', count: '计数', raw: '原始' }[result.value.aggregate] || result.value.aggregate));

const seriesList = computed(() =>
	columns.value.map((col, idx) => ({
		name: col.name,
		type: 'line',
		showSymbol: false,
		smooth: true,
		itemStyle: { color: COLORS[idx % COLORS.length] },
		data: rows.value
			.map((r) => [toTs(r.time), r.values[col.variableId]])
			.filter((p) => p[1] !== null && p[1] !== undefined),
	}))
);

const chartOption = computed(() => ({
	tooltip: { trigger: 'axis' },
	legend: { top: 0, type: 'scroll' },
	grid: { left: 16, right: 24, top: 40, bottom: 40, containLabel: true },
	xAxis: { type: 'time' },
	yAxis: { type: 'value', scale: true },
	dataZoom: [{ type: 'inside' }, { type: 'slider', height: 16, bottom: 8 }],
	series: seriesList.value,
}));

function toTs(t) {
	if (typeof t === 'number') return t;
	if (t instanceof Date) return t.getTime();
	return new Date(String(t).replace(' ', 'T')).getTime();
}

function fmtVal(v) {
	if (v === null || v === undefined) return '-';
	return Math.round(v * 1000) / 1000;
}

function formatTime(t) {
	if (!t) return '-';
	const d = t instanceof Date ? t : new Date(t);
	if (isNaN(d.getTime())) return String(t);
	const p = (n) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function toIso(t) {
	if (!t) return t;
	if (t instanceof Date) return t.toISOString();
	const d = new Date(String(t).replace(' ', 'T'));
	return isNaN(d.getTime()) ? t : d.toISOString();
}

function fmtLocal(d) {
	const p = (n) => String(n).padStart(2, '0');
	return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function applyQuickRange(key) {
	const end = new Date();
	const start = new Date();
	if (key === '24h') start.setDate(start.getDate() - 1);
	else if (key === '7d') start.setDate(start.getDate() - 7);
	else if (key === '30d') start.setDate(start.getDate() - 30);
	else return;
	timeRange.value = [fmtLocal(start), fmtLocal(end)];
}

async function loadDevices() {
	const res = await iotDeviceApi.list();
	devices.value = res?.data?.result || [];
}

async function onDeviceChange() {
	variableIds.value = [];
	variables.value = [];
	result.value = {};
	if (!deviceId.value) return;
	const res = await iotDeviceVariableApi.list(deviceId.value);
	variables.value = res?.data?.result || [];
}

async function loadReport() {
	if (!canQuery.value) return;
	loading.value = true;
	try {
		const payload = {
			deviceId: deviceId.value,
			variableIds: variableIds.value,
			aggregate: aggregate.value,
			granularity: granularity.value,
		};
		if (timeRange.value && timeRange.value.length === 2) {
			payload.startTime = toIso(timeRange.value[0]);
			payload.endTime = toIso(timeRange.value[1]);
		}
		const res = await iotReportApi.query(payload);
		result.value = res?.data?.result || {};
	} finally {
		loading.value = false;
	}
}

function exportCsv() {
	const cols = columns.value;
	const header = ['时间', ...cols.map((c) => c.name)];
	const lines = [header.map(csvCell).join(',')];
	rows.value.forEach((r) => {
		lines.push([formatTime(r.time), ...cols.map((c) => fmtVal(r.values[c.variableId]))].map(csvCell).join(','));
	});
	const csv = '\uFEFF' + lines.join('\r\n');
	const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
	const url = URL.createObjectURL(blob);
	const a = document.createElement('a');
	a.href = url;
	a.download = `report_${result.value.deviceName || 'device'}_${Date.now()}.csv`;
	a.click();
	URL.revokeObjectURL(url);
	ElMessage.success('已导出 CSV');
}

function csvCell(v) {
	const s = v === null || v === undefined ? '' : String(v);
	return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

onMounted(() => {
	applyQuickRange('24h');
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
