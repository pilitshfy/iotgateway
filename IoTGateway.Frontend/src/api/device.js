import request from '@/utils/request'

// 获取设备列表
export function getDevices(params) {
  return request({
    url: '/api/BasicData/Device/Search',
    method: 'post',
    data: params
  })
}

// 获取设备详情
export function getDevice(id) {
  return request({
    url: `/api/BasicData/Device/${id}`,
    method: 'get'
  })
}

// 创建设备
export function createDevice(data) {
  return request({
    url: '/api/BasicData/Device/Create',
    method: 'post',
    data
  })
}

// 更新设备
export function updateDevice(data) {
  return request({
    url: '/api/BasicData/Device/Edit',
    method: 'post',
    data
  })
}

// 删除设备
export function deleteDevice(id) {
  return request({
    url: `/api/BasicData/Device/${id}`,
    method: 'delete'
  })
}

// 批量删除设备
export function batchDeleteDevices(ids) {
  return request({
    url: '/api/BasicData/Device/BatchDelete',
    method: 'post',
    data: { ids }
  })
}

// 获取驱动方法
export function getDriverMethods(id) {
  return request({
    url: `/api/BasicData/Device/GetMethods?ID=${id}`,
    method: 'get'
  })
}
