import request from '@/utils/request'

// 获取驱动列表
export function getDrivers(params) {
  return request({
    url: '/BasicData/Driver/Search',
    method: 'post',
    data: params
  })
}

// 获取驱动详情
export function getDriver(id) {
  return request({
    url: `/BasicData/Driver/${id}`,
    method: 'get'
  })
}

// 创建驱动
export function createDriver(data) {
  return request({
    url: '/BasicData/Driver/Create',
    method: 'post',
    data
  })
}

// 更新驱动
export function updateDriver(data) {
  return request({
    url: '/BasicData/Driver/Edit',
    method: 'post',
    data
  })
}

// 删除驱动
export function deleteDriver(id) {
  return request({
    url: `/BasicData/Driver/${id}`,
    method: 'delete'
  })
}
