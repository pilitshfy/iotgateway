import request from '@/utils/request'

// 登录
export function login(data) {
  return request({
    url: '/login',
    method: 'post',
    data
  })
}

// 登出
export function logout() {
  return request({
    url: '/login/logout',
    method: 'post'
  })
}

// 获取当前用户信息
export function getCurrentUser() {
  return request({
    url: '/Framework/GetCurrentUserInfo',
    method: 'get'
  })
}

// 获取菜单
export function getMenu() {
  return request({
    url: '/Framework/Menu',
    method: 'get'
  })
}
