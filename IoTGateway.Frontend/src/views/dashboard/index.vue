<template>
  <div class="dashboard">
    <el-row :gutter="20">
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-icon" style="background: #409EFF;">
            <el-icon :size="32"><Connection /></el-icon>
          </div>
          <div class="stat-info">
            <div class="stat-value">{{ stats.deviceCount }}</div>
            <div class="stat-label">设备总数</div>
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-icon" style="background: #67C23A;">
            <el-icon :size="32"><CircleCheck /></el-icon>
          </div>
          <div class="stat-info">
            <div class="stat-value">{{ stats.onlineCount }}</div>
            <div class="stat-label">在线设备</div>
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-icon" style="background: #E6A23C;">
            <el-icon :size="32"><Setting /></el-icon>
          </div>
          <div class="stat-info">
            <div class="stat-value">{{ stats.driverCount }}</div>
            <div class="stat-label">驱动数量</div>
          </div>
        </div>
      </el-col>
      <el-col :span="6">
        <div class="stat-card">
          <div class="stat-icon" style="background: #F56C6C;">
            <el-icon :size="32"><Warning /></el-icon>
          </div>
          <div class="stat-info">
            <div class="stat-value">{{ stats.offlineCount }}</div>
            <div class="stat-label">离线设备</div>
          </div>
        </div>
      </el-col>
    </el-row>

    <el-row :gutter="20" style="margin-top: 20px;">
      <el-col :span="12">
        <el-card>
          <template #header>
            <span>设备状态分布</span>
          </template>
          <div ref="pieChartRef" style="height: 300px;"></div>
        </el-card>
      </el-col>
      <el-col :span="12">
        <el-card>
          <template #header>
            <span>最近活动</span>
          </template>
          <el-table :data="recentActivities" style="height: 300px;">
            <el-table-column prop="time" label="时间" width="160" />
            <el-table-column prop="device" label="设备" />
            <el-table-column prop="action" label="操作" />
            <el-table-column prop="status" label="状态" width="80">
              <template #default="{ row }">
                <el-tag :type="row.status === '成功' ? 'success' : 'danger'">
                  {{ row.status }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'

const stats = reactive({
  deviceCount: 0,
  onlineCount: 0,
  driverCount: 0,
  offlineCount: 0
})

const recentActivities = ref([
  { time: '2024-01-15 10:30', device: 'CNC-001', action: '数据采集', status: '成功' },
  { time: '2024-01-15 10:25', device: 'PLC-001', action: '远程控制', status: '成功' },
  { time: '2024-01-15 10:20', device: 'Sensor-01', action: '数据采集', status: '失败' },
  { time: '2024-01-15 10:15', device: 'Robot-01', action: '状态上报', status: '成功' }
])

onMounted(() => {
  // 实际项目中从API获取数据
  stats.deviceCount = 25
  stats.onlineCount = 18
  stats.driverCount = 8
  stats.offlineCount = 7
})
</script>

<style scoped>
.dashboard {
  padding: 20px;
}

.stat-card {
  display: flex;
  align-items: center;
  padding: 20px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.1);
}

.stat-icon {
  width: 60px;
  height: 60px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  margin-right: 20px;
}

.stat-info {
  flex: 1;
}

.stat-value {
  font-size: 28px;
  font-weight: bold;
  color: #333;
}

.stat-label {
  font-size: 14px;
  color: #999;
  margin-top: 5px;
}
</style>
