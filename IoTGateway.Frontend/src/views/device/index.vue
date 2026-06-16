<template>
  <div class="device-container">
    <el-card>
      <template #header>
        <div class="card-header">
          <span>设备列表</span>
          <el-button type="primary" @click="handleCreate">创建设备</el-button>
        </div>
      </template>

      <!-- 搜索表单 -->
      <el-form :inline="true" :model="searchForm" class="search-form">
        <el-form-item label="设备名称">
          <el-input v-model="searchForm.Name" placeholder="请输入设备名称" clearable />
        </el-form-item>
        <el-form-item label="设备类型">
          <el-select v-model="searchForm.DeviceTypeEnum" placeholder="请选择" clearable>
            <el-option label="设备" :value="0" />
            <el-option label="分组" :value="1" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="handleSearch">搜索</el-button>
          <el-button @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>

      <!-- 表格 -->
      <el-table :data="tableData" v-loading="loading" stripe>
        <el-table-column prop="Name" label="设备名称" />
        <el-table-column prop="Code" label="设备编号" />
        <el-table-column prop="DriverName" label="驱动" />
        <el-table-column prop="OnlineTime" label="在线时间" />
        <el-table-column prop="CreateTime" label="创建时间" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.IsOnline ? 'success' : 'danger'">
              {{ row.IsOnline ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="handleDetail(row)">详情</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 分页 -->
      <el-pagination
        v-model:current-page="pagination.page"
        v-model:page-size="pagination.pageSize"
        :total="pagination.total"
        :page-sizes="[10, 20, 50, 100]"
        layout="total, sizes, prev, pager, next, jumper"
        style="margin-top: 20px; justify-content: flex-end;"
      />
    </el-card>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getDevices, deleteDevice } from '@/api/device'

const loading = ref(false)
const tableData = ref([])

const searchForm = reactive({
  Name: '',
  DeviceTypeEnum: null
})

const pagination = reactive({
  page: 1,
  pageSize: 20,
  total: 0
})

const loadData = async () => {
  loading.value = true
  try {
    const params = {
      ...searchForm,
      page: pagination.page,
      pageSize: pagination.pageSize
    }
    const res = await getDevices(params)
    if (res && res.Data) {
      tableData.value = res.Data
      pagination.total = res.Count || 0
    }
  } catch (error) {
    console.error('加载数据失败:', error)
  } finally {
    loading.value = false
  }
}

const handleSearch = () => {
  pagination.page = 1
  loadData()
}

const handleReset = () => {
  searchForm.Name = ''
  searchForm.DeviceTypeEnum = null
  handleSearch()
}

const handleCreate = () => {
  // 实际项目中打开创建对话框
  ElMessage.info('打开创建设备对话框')
}

const handleEdit = (row) => {
  ElMessage.info(`编辑设备: ${row.Name}`)
}

const handleDetail = (row) => {
  ElMessage.info(`查看设备详情: ${row.Name}`)
}

const handleDelete = async (row) => {
  try {
    await ElMessageBox.confirm(`确定要删除设备 "${row.Name}" 吗？`, '提示', {
      type: 'warning'
    })
    await deleteDevice(row.ID)
    ElMessage.success('删除成功')
    loadData()
  } catch (error) {
    if (error !== 'cancel') {
      console.error('删除失败:', error)
    }
  }
}

onMounted(() => {
  loadData()
})
</script>

<style scoped>
.device-container {
  padding: 20px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.search-form {
  margin-bottom: 20px;
}
</style>
