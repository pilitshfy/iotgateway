<template>
  <div class="driver-container">
    <el-card>
      <template #header>
        <div class="card-header">
          <span>驱动列表</span>
          <el-button type="primary" @click="handleCreate">添加驱动</el-button>
        </div>
      </template>

      <el-table :data="tableData" v-loading="loading" stripe>
        <el-table-column prop="Name" label="驱动名称" />
        <el-table-column prop="TypeName" label="驱动类型" />
        <el-table-column prop="DeviceCount" label="关联设备" />
        <el-table-column prop="CreateTime" label="创建时间" />
        <el-table-column label="操作" width="150">
          <template #default="{ row }">
            <el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { getDrivers } from '@/api/driver'

const loading = ref(false)
const tableData = ref([])

const loadData = async () => {
  loading.value = true
  try {
    const res = await getDrivers({})
    if (res && res.Data) {
      tableData.value = res.Data
    }
  } catch (error) {
    console.error('加载数据失败:', error)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  ElMessage.info('打开添加驱动对话框')
}

const handleEdit = (row) => {
  ElMessage.info(`编辑驱动: ${row.Name}`)
}

const handleDelete = (row) => {
  ElMessage.info(`删除驱动: ${row.Name}`)
}

onMounted(() => {
  loadData()
})
</script>

<style scoped>
.driver-container {
  padding: 20px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>
