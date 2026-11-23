<template>
	<div class="iot-page" v-loading="loading">
		<div class="dashboard-header">
			<div>
				<h2>IoT 告警中心</h2>
				<span class="subtitle">实时掌握告警态势与恢复进展</span>
			</div>
			<div class="dashboard-tools">
				<el-tree-select
					v-model="orgId"
					:data="orgTree"
					:props="orgTreeProps"
					node-key="id"
					check-strictly
					default-expand-all
					filterable
					clearable
					placeholder="全部组织（含下级）"
					style="width: 220px"
					@change="loadData"
				/>
				<el-select v-model="refreshSeconds" style="width: 120px" @change="restartTimer">
					<el-option label="手动刷新" :value="0" />
					<el-option label="10 秒刷新" :value="10" />
					<el-option label="30 秒刷新" :value="30" />
					<el-option label="60 秒刷新" :value="60" />
				</el-select>
				<el-button type="primary" :loading="refreshing" @click="loadData">立即刷新</el-button>
			</div>
		</div>
		<div class="update-time">最近更新：{{ updateTime || '-' }}</div>

		<el-row :gutter="12" class="iot-stats">
			<el-col :xs="12" :sm="12" :md="6">
				<el-card shadow="never" class="metric-card active"><div class="stat-label">活动中 Active</div><div class="stat-value">{{ overview.activeCount || 0 }}</div><div class="stat-note">待确认的当前告警</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="12" :md="6">
				<el-card shadow="never" class="metric-card ack"><div class="stat-label">已确认 Ack</div><div class="stat-value">{{ overview.acknowledgedCount || 0 }}</div><div class="stat-note">已知晓，等待恢复</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="12" :md="6">
				<el-card shadow="never" class="metric-card today"><div class="stat-label">今日告警 Today</div><div class="stat-value">{{ overview.todayAlarmCount || 0 }}</div><div class="stat-note">今日首次触发的事件</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="12" :md="6">
				<el-card shadow="never" class="metric-card recovered"><div class="stat-label">今日恢复 Recovered</div><div class="stat-value">{{ overview.todayRecoveredCount || 0 }}</div><div class="stat-note">今日恢复的事件</div></el-card>
			</el-col>
		</el-row>

		<el-row :gutter="12">
			<el-col :xs="24" :lg="16">
				<el-card shadow="never" class="chart-card">
					<template #header><div class="card-header"><span>24 小时告警趋势</span><span class="card-hint">按首次触发时间统计</span></div></template>
					<scEcharts height="340px" :option="trendOption" />
				</el-card>
			</el-col>
			<el-col :xs="24" :lg="8">
				<el-card shadow="never" class="chart-card">
					<template #header><div class="card-header"><span>今日告警级别分布</span><span class="card-hint">提示 / 一般 / 严重 / 紧急</span></div></template>
					<scEcharts height="340px" :option="severityOption" />
				</el-card>
			</el-col>
		</el-row>

		<el-card shadow="never" class="latest-card">
			<template #header><div class="card-header"><span>最新告警事件</span><span class="card-hint">按最近触发时间排序</span></div></template>
			<el-table :data="overview.latestEvents || []" border stripe>
				<el-table-column prop="ruleName" label="告警规则" min-width="160" show-overflow-tooltip />
				<el-table-column prop="deviceName" label="设备" min-width="140" show-overflow-tooltip />
				<el-table-column label="级别" width="90"><template #default="{ row }"><el-tag :type="severityMeta(row.severity).type">{{ severityMeta(row.severity).label }}</el-tag></template></el-table-column>
				<el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="statusMeta(row.status).type">{{ statusMeta(row.status).label }}</el-tag></template></el-table-column>
				<el-table-column prop="lastTriggerTime" label="最近触发" width="175" />
				<el-table-column prop="recoverTime" label="恢复时间" width="175"><template #default="{ row }">{{ row.recoverTime || '-' }}</template></el-table-column>
				<el-table-column prop="triggerCount" label="触发次数" width="95" align="center" />
				<el-table-column prop="message" label="事件描述" min-width="240" show-overflow-tooltip />
			</el-table>
			<el-empty v-if="!(overview.latestEvents || []).length" description="暂无告警事件" :image-size="80" />
		</el-card>
	</div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue';
import scEcharts from '/@/components/scEcharts/index.vue';
import { iotAlarmEventApi } from '/@/api/iot';
import { getAPI } from '/@/utils/axios-utils';
import { SysOrgApi } from '/@/api-services/api';

const severityOptions = [
	{ value: 0, label: '提示', type: 'info', color: '#909399', names: ['Info', '提示'] },
	{ value: 1, label: '一般', type: 'warning', color: '#E6A23C', names: ['Warning', '一般'] },
	{ value: 2, label: '严重', type: 'danger', color: '#F56C6C', names: ['Critical', '严重'] },
	{ value: 3, label: '紧急', type: 'danger', color: '#B91C1C', names: ['Emergency', '紧急'] },
];
const statusOptions = [
	{ value: 0, label: '活动中', type: 'danger', names: ['Active', '活动中'] },
	{ value: 1, label: '已确认', type: 'warning', names: ['Acknowledged', '已确认'] },
	{ value: 2, label: '已恢复', type: 'success', names: ['Recovered', '已恢复'] },
	{ value: 3, label: '已闭环', type: 'info', names: ['Closed', '已闭环'] },
];
const orgTreeProps = { label: 'name', children: 'children', disabled: 'disabled' };
const orgTree = ref([]);
const orgId = ref(null);
const overview = ref({});
const loading = ref(false);
const refreshing = ref(false);
const refreshSeconds = ref(30);
const updateTime = ref('');
let timer = null;
let requesting = false;

const metaOf = (options, value) => {
	const numeric = Number(value);
	return options.find((item) => item.value === numeric || item.value === value || item.names.includes(value)) || { label: value ?? '-', type: 'info', color: '#909399' };
};
const severityMeta = (value) => metaOf(severityOptions, value);
const statusMeta = (value) => metaOf(statusOptions, value);
const trendOption = ref({ tooltip: {}, xAxis: {}, yAxis: {}, series: [] });
const severityOption = ref({ tooltip: {}, series: [] });

const buildCharts = () => {
	const trend = overview.value.hourlyTrend || [];
	const distribution = severityOptions.map((severity) => {
		const point = (overview.value.severityDistribution || []).find((item) => severityMeta(item.category).value === severity.value);
		return { name: severity.label, value: point?.value || 0, itemStyle: { color: severity.color } };
	});
	trendOption.value = {
		tooltip: { trigger: 'axis' },
		grid: { left: 20, right: 24, top: 28, bottom: 24, containLabel: true },
		xAxis: { type: 'category', boundaryGap: false, data: trend.map((item) => item.category), axisLabel: { color: '#7a8499' } },
		yAxis: { type: 'value', minInterval: 1, name: '告警数', splitLine: { lineStyle: { type: 'dashed' } } },
		series: [
			{
				name: '告警数',
				type: 'line',
				smooth: true,
				showSymbol: false,
				data: trend.map((item) => item.value),
				lineStyle: { width: 3, color: '#5470c6' },
				itemStyle: { color: '#5470c6' },
				areaStyle: { color: { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: 'rgba(84,112,198,0.35)' }, { offset: 1, color: 'rgba(84,112,198,0.02)' }] } },
			},
		],
	};
	severityOption.value = {
		tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
		legend: { bottom: 8, icon: 'circle' },
		series: [{ name: '告警级别', type: 'pie', radius: ['46%', '70%'], center: ['50%', '44%'], avoidLabelOverlap: true, label: { formatter: '{b}\n{c} 条' }, data: distribution }],
	};
};
const formatNow = () => {
	const date = new Date();
	const pad = (value) => String(value).padStart(2, '0');
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
};
const loadOrgs = async () => {
	const res = await getAPI(SysOrgApi).apiSysOrgTreeGet(0);
	orgTree.value = res.data.result || [];
};
const loadData = async () => {
	if (requesting) return;
	requesting = true;
	if (!updateTime.value) loading.value = true;
	else refreshing.value = true;
	try {
		const res = await iotAlarmEventApi.overview({ orgId: orgId.value });
		overview.value = res.data.result || {};
		updateTime.value = formatNow();
		buildCharts();
	} finally {
		loading.value = false;
		refreshing.value = false;
		requesting = false;
	}
};
const restartTimer = () => {
	if (timer) clearInterval(timer);
	timer = null;
	if (refreshSeconds.value > 0) timer = setInterval(loadData, refreshSeconds.value * 1000);
};

onMounted(async () => {
	await Promise.all([loadOrgs(), loadData()]);
	restartTimer();
});
onBeforeUnmount(() => {
	if (timer) clearInterval(timer);
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
	background: var(--el-bg-color-page);
	min-height: 100%;
}
.dashboard-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	gap: 16px;
}
.dashboard-header h2 {
	margin: 0 0 5px;
	font-size: 24px;
	font-weight: 600;
}
.subtitle,
.update-time,
.card-hint {
	color: var(--el-text-color-secondary);
	font-size: 13px;
}
.dashboard-tools {
	display: flex;
	align-items: center;
	gap: 10px;
}
.update-time {
	margin: 8px 0 12px;
	text-align: right;
}
.iot-stats {
	margin-bottom: 4px;
}
.metric-card {
	margin-bottom: 12px;
	border-top: 3px solid transparent;
}
.metric-card.active {
	border-top-color: #f56c6c;
}
.metric-card.ack {
	border-top-color: #e6a23c;
}
.metric-card.today {
	border-top-color: #5470c6;
}
.metric-card.recovered {
	border-top-color: #67c23a;
}
.stat-label {
	font-size: 14px;
	color: var(--el-text-color-secondary);
}
.stat-value {
	margin: 8px 0 4px;
	font-size: 34px;
	line-height: 42px;
	font-weight: 650;
}
.active .stat-value {
	color: #f56c6c;
}
.ack .stat-value {
	color: #e6a23c;
}
.today .stat-value {
	color: #5470c6;
}
.recovered .stat-value {
	color: #67c23a;
}
.stat-note {
	font-size: 12px;
	color: var(--el-text-color-secondary);
}
.chart-card,
.latest-card {
	margin-bottom: 12px;
}
.card-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	font-weight: 600;
}
.card-hint {
	font-weight: 400;
}
@media (max-width: 900px) {
	.dashboard-header,
	.dashboard-tools {
		align-items: flex-start;
		flex-direction: column;
	}
	.dashboard-tools {
		width: 100%;
	}
}
</style>
