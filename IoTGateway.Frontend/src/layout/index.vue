<template>
  <div class="layout-container">
    <!-- 侧边栏 -->
    <div class="sidebar" :class="{ 'is-collapse': isCollapse }">
      <div class="logo">
        <img src="@/assets/logo.png" alt="logo" v-if="!isCollapse" />
        <span v-if="!isCollapse">IoT Gateway</span>
        <el-icon v-else><Box /></el-icon>
      </div>
      <el-menu
        :default-active="activeMenu"
        :collapse="isCollapse"
        :router="true"
        background-color="#20222A"
        text-color="#909399"
        active-text-color="#fff"
      >
        <el-menu-item index="/dashboard">
          <el-icon><HomeFilled /></el-icon>
          <template #title>首页</template>
        </el-menu-item>
        <el-menu-item index="/device">
          <el-icon><Connection /></el-icon>
          <template #title>设备管理</template>
        </el-menu-item>
        <el-menu-item index="/driver">
          <el-icon><Setting /></el-icon>
          <template #title>驱动管理</template>
        </el-menu-item>
        <el-menu-item index="/config">
          <el-icon><Tools /></el-icon>
          <template #title>系统配置</template>
        </el-menu-item>
        <el-menu-item index="/mqtt">
          <el-icon><Message /></el-icon>
          <template #title>MQTT客户端</template>
        </el-menu-item>
        <el-menu-item index="/rpc">
          <el-icon><Document /></el-icon>
          <template #title>RPC日志</template>
        </el-menu-item>
      </el-menu>
    </div>

    <!-- 主内容区 -->
    <div class="main-container">
      <!-- 顶部导航 -->
      <div class="navbar">
        <div class="left">
          <el-icon class="collapse-btn" @click="toggleCollapse">
            <Fold v-if="!isCollapse" />
            <Expand v-else />
          </el-icon>
          <el-breadcrumb separator="/">
            <el-breadcrumb-item :to="{ path: '/' }">首页</el-breadcrumb-item>
            <el-breadcrumb-item v-if="route.meta.title">
              {{ route.meta.title }}
            </el-breadcrumb-item>
          </el-breadcrumb>
        </div>
        <div class="right">
          <el-dropdown @command="handleCommand">
            <span class="user-info">
              <el-avatar :size="32" icon="UserFilled" />
              <span class="username">{{ userStore.userInfo.Name || 'Admin' }}</span>
              <el-icon><ArrowDown /></el-icon>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="changePassword">修改密码</el-dropdown-item>
                <el-dropdown-item command="logout" divided>退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </div>

      <!-- 内容区 -->
      <div class="content">
        <router-view />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { useUserStore } from '@/stores/user'
import { logout } from '@/api/login'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()

const isCollapse = ref(false)

const activeMenu = computed(() => route.path)

const toggleCollapse = () => {
  isCollapse.value = !isCollapse.value
}

const handleCommand = async (command) => {
  if (command === 'logout') {
    try {
      await ElMessageBox.confirm('确定要退出登录吗？', '提示', {
        type: 'warning'
      })
      await logout()
    } catch (e) {
      // 用户取消
    }
    userStore.logout()
    router.push('/login')
  } else if (command === 'changePassword') {
    // 修改密码
  }
}
</script>

<style scoped>
.layout-container {
  display: flex;
  width: 100%;
  height: 100vh;
}

.sidebar {
  width: 200px;
  height: 100vh;
  background-color: #20222A;
  transition: width 0.3s;
  overflow: hidden;
}

.sidebar.is-collapse {
  width: 64px;
}

.logo {
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 18px;
  font-weight: bold;
  border-bottom: 1px solid #333;
}

.logo img {
  width: 32px;
  height: 32px;
  margin-right: 8px;
}

.sidebar .el-menu {
  border-right: none;
}

.main-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.navbar {
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 20px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
}

.navbar .left {
  display: flex;
  align-items: center;
}

.collapse-btn {
  font-size: 20px;
  cursor: pointer;
  margin-right: 20px;
}

.user-info {
  display: flex;
  align-items: center;
  cursor: pointer;
}

.username {
  margin: 0 8px;
}

.content {
  flex: 1;
  padding: 20px;
  overflow: auto;
  background: #f5f5f5;
}
</style>
