<template>
  <div class="mqtt-container">
    <el-card>
      <template #header>
        <div class="card-header">
          <span>MQTT客户端</span>
          <el-button type="primary">连接</el-button>
        </div>
      </template>
      <el-table :data="tableData" v-loading="loading" stripe>
        <el-table-column prop="ClientId" label="客户端ID" />
        <el-table-column prop="ServerUrl" label="服务器地址" />
        <el-table-column prop="Status" label="状态">
          <template #default="{ row }">
            <el-tag :type="row.Status === '已连接' ? 'success' : 'info'">
              {{ row.Status }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100">
          <template #default="{ row }">
            <el-button link type="danger">断开</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'

const loading = ref(false)
const tableData = ref([])

onMounted(() => {
  // loadData()
})
</script>

<style scoped>
.mqtt-container {
  padding: 20px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>
