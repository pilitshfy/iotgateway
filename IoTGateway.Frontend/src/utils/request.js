import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'

const request = axios.create({
  baseURL: '/api',
  timeout: 30000
})

// 从 localStorage 获取 token
function getToken() {
  return localStorage.getItem('token') || ''
}

// 显示消息（需要异步调用，避免在 axios 拦截器中同步调用导致消息不显示）
function showMessage(message, type = 'error') {
  setTimeout(() => {
    ElMessage({ message, type, duration: 0, showClose: true })
  }, 0)
}

// 请求拦截器
request.interceptors.request.use(
  config => {
    const token = getToken()
    if (token) {
      config.headers['Authorization'] = `Bearer ${token}`
    }
    return config
  },
  error => {
    return Promise.reject(error)
  }
)

// 响应拦截器
request.interceptors.response.use(
  response => {
    const res = response.data
    if (typeof res !== 'object' || res === null) {
      return res
    }
    if (res.Code && res.Code !== 200) {
      showMessage(res.Msg || '请求失败')
      return Promise.reject(new Error(res.Msg || '请求失败'))
    }
    return res
  },
  error => {
    if (error.response) {
      const status = error.response.status
      if (status === 401) {
        localStorage.removeItem('token')
        localStorage.removeItem('userInfo')
        router.push('/login')
        showMessage('登录已过期，请重新登录')
      } else {
        let msg = '请求失败'
        const data = error.response.data
        if (data && typeof data === 'object') {
          msg = data.Msg || data.message || `请求失败 (${status})`
        } else {
          msg = `请求失败 (${status})`
        }
        showMessage(msg)
      }
    } else if (error.request) {
      showMessage('网络错误，请检查后端服务是否启动')
    } else {
      showMessage(error.message || '请求配置错误')
    }
    return Promise.reject(error)
  }
)

export default request
