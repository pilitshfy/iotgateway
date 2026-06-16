<template>
  <div class="rpc-container">
    <el-card>
      <template #header>
        <span>RPC日志</span>
      </template>
      <el-table :data="tableData" v-loading="loading" stripe>
        <el-table-column prop="CreateTime" label="时间" width="160" />
        <el-table-column prop="DeviceName" label="设备" />
        <el-table-column prop="Method" label="方法" />
        <el-table-column prop="Request" label="请求参数" show-overflow-tooltip />
        <el-table-column prop="Response" label="响应结果" show-overflow-tooltip />
        <el-table-column prop="Status" label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.Status === '成功' ? 'success' : 'danger'">
              {{ row.Status }}
            </el-tag>
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
.rpc-container {
  padding: 20px;
}
</style>
