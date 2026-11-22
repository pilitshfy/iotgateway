import request from '/@/utils/request';

/**
 * IoT 设备
 */
export const iotDeviceApi = {
	page: (data: any) => request({ url: '/api/iotDevice/page', method: 'post', data }),
	list: (params?: any) => request({ url: '/api/iotDevice/list', method: 'get', params }),
	detail: (id: number) => request({ url: '/api/iotDevice/detail', method: 'get', params: { id } }),
	add: (data: any) => request({ url: '/api/iotDevice/add', method: 'post', data }),
	update: (data: any) => request({ url: '/api/iotDevice/update', method: 'post', data }),
	delete: (id: number) => request({ url: '/api/iotDevice/delete', method: 'post', data: { id } }),
	batchDelete: (ids: number[]) => request({ url: '/api/iotDevice/batchDelete', method: 'post', data: { ids } }),
	batchUpdate: (data: any) => request({ url: '/api/iotDevice/batchUpdate', method: 'post', data }),
	copy: (data: { id: number; count: number }) => request({ url: '/api/iotDevice/copy', method: 'post', data }),
	requestAttributes: (id: number) => request({ url: '/api/iotDevice/requestAttributes', method: 'post', data: { id } }),
	export: () => request({ url: '/api/iotDevice/export', method: 'get', responseType: 'blob' }),
	importBackup: (file: File) => {
		const data = new FormData();
		data.append('file', file);
		return request({ url: '/api/iotDevice/import', method: 'post', data });
	},
	driverList: () => request({ url: '/api/iotDevice/driverList', method: 'get' }),
	stats: () => request({ url: '/api/iotDevice/stats', method: 'get' }),
};

/**
 * IoT 驱动
 */
export const iotDriverApi = {
	page: (data: any) => request({ url: '/api/iotDriver/page', method: 'post', data }),
	list: () => request({ url: '/api/iotDriver/list', method: 'get' }),
	add: (data: any) => request({ url: '/api/iotDriver/add', method: 'post', data }),
	update: (data: any) => request({ url: '/api/iotDriver/update', method: 'post', data }),
	delete: (id: number) => request({ url: '/api/iotDriver/delete', method: 'post', data: { id } }),
	batchDelete: (ids: number[]) => request({ url: '/api/iotDriver/batchDelete', method: 'post', data: { ids } }),
	batchUpdate: (data: any) => request({ url: '/api/iotDriver/batchUpdate', method: 'post', data }),
	driverFiles: () => request({ url: '/api/iotDriver/driverFiles', method: 'get' }),
	driverConfigs: (assembleName: string) => request({ url: '/api/iotDriver/driverConfigs', method: 'get', params: { assembleName } }),
	driverMethods: (assembleName: string) => request({ url: '/api/iotDriver/driverMethods', method: 'get', params: { assembleName } }),
	reload: () => request({ url: '/api/iotDriver/reload', method: 'post' }),
};

/**
 * 设备通讯配置
 */
export const iotDeviceConfigApi = {
	page: (data: any) => request({ url: '/api/iotDeviceConfig/page', method: 'post', data }),
	list: (deviceId: number) => request({ url: '/api/iotDeviceConfig/list', method: 'get', params: { deviceId } }),
	add: (data: any) => request({ url: '/api/iotDeviceConfig/add', method: 'post', data }),
	update: (data: any) => request({ url: '/api/iotDeviceConfig/update', method: 'post', data }),
	delete: (id: number) => request({ url: '/api/iotDeviceConfig/delete', method: 'post', data: { id } }),
	batchUpdate: (data: any) => request({ url: '/api/iotDeviceConfig/batchUpdate', method: 'post', data }),
	export: (deviceId?: number) => request({ url: '/api/iotDeviceConfig/export', method: 'get', params: { deviceId }, responseType: 'blob' }),
};

/**
 * 设备变量
 */
export const iotDeviceVariableApi = {
	page: (data: any) => request({ url: '/api/iotDeviceVariable/page', method: 'post', data }),
	list: (deviceId: number) => request({ url: '/api/iotDeviceVariable/list', method: 'get', params: { deviceId } }),
	add: (data: any) => request({ url: '/api/iotDeviceVariable/add', method: 'post', data }),
	update: (data: any) => request({ url: '/api/iotDeviceVariable/update', method: 'post', data }),
	write: (data: { deviceId: number; values: Record<string, any> }) => request({ url: '/api/iotDeviceVariable/write', method: 'post', data }),
	delete: (id: number) => request({ url: '/api/iotDeviceVariable/delete', method: 'post', data: { id } }),
	batchUpdate: (data: any) => request({ url: '/api/iotDeviceVariable/batchUpdate', method: 'post', data }),
	export: (deviceId?: number) => request({ url: '/api/iotDeviceVariable/export', method: 'get', params: { deviceId }, responseType: 'blob' }),
	import: (file: File, deviceId?: number) => {
		const data = new FormData();
		data.append('file', file);
		if (deviceId) data.append('deviceId', String(deviceId));
		return request({ url: '/api/iotDeviceVariable/import', method: 'post', data });
	},
};

/**
 * 系统传输配置
 */
export const iotSystemConfigApi = {
	get: () => request({ url: '/api/iotSystemConfig/get', method: 'get' }),
	save: (data: any) => request({ url: '/api/iotSystemConfig/save', method: 'post', data }),
};

/**
 * RPC 日志
 */
export const iotRpcLogApi = {
	page: (data: any) => request({ url: '/api/iotRpcLog/page', method: 'post', data }),
	delete: (id: number) => request({ url: '/api/iotRpcLog/delete', method: 'post', data: { id } }),
	batchDelete: (ids: number[]) => request({ url: '/api/iotRpcLog/batchDelete', method: 'post', data: { ids } }),
	clear: () => request({ url: '/api/iotRpcLog/clear', method: 'post' }),
	detail: (id: number) => request({ url: '/api/iotRpcLog/detail', method: 'get', params: { id } }),
	export: (params?: any) => request({ url: '/api/iotRpcLog/export', method: 'get', params, responseType: 'blob' }),
};

/**
 * IoT 历史数据（TimescaleDB）
 */
export const iotHistoryApi = {
	query: (data: any) => request({ url: '/api/iotHistory/queryHistory', method: 'post', data }),
};

/**
 * IoT 首页图表
 */
export const iotDashboardApi = {
	deviceStatus: () => request({ url: '/api/iotDashboard/deviceStatusChart', method: 'get' }),
	variableStatus: () => request({ url: '/api/iotDashboard/variableStatusChart', method: 'get' }),
};

/**
 * MQTT 运行状态
 */
export const iotMqttApi = {
	status: () => request({ url: '/api/iotMqtt/status', method: 'get' }),
	publish: (data: any) => request({ url: '/api/iotMqtt/publish', method: 'post', data }),
};

/**
 * RPC 调试
 */
export const iotRpcApi = {
	invoke: (data: any) => request({ url: '/api/iotRpcInvoke/invoke', method: 'post', data }),
};
