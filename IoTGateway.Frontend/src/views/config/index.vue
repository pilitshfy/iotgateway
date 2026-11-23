<template>
  <div class="config-container">
    <el-card>
      <template #header>
        <span>系统配置</span>
      </template>
      <el-table :data="tableData" v-loading="loading" stripe>
        <el-table-column prop="Name" label="配置名称" />
        <el-table-column prop="Value" label="配置值" />
        <el-table-column prop="Remark" label="备注" />
        <el-table-column label="操作" width="150">
          <template #default="{ row }">
            <el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'

const loading = ref(false)
const tableData = ref([
  { Name: '系统名称', Value: 'IoT Gateway', Remark: '系统显示名称' },
  { Name: '数据采集间隔', Value: '1000', Remark: '单位：毫秒' }
])

const handleEdit = (row) => {
  ElMessage.info(`编辑配置: ${row.Name}`)
}

onMounted(() => {
  // loadData()
})
</script>

<style scoped>
.config-container {
  padding: 20px;
}
</style>
