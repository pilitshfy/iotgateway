<template>
	<div class="iot-page">
		<el-row :gutter="12" class="iot-stats">
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">设备总数</div><div class="stat-value">{{ stats.deviceCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">在线设备</div><div class="stat-value online">{{ stats.onlineCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">离线设备</div><div class="stat-value offline">{{ stats.offlineCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">分组数量</div><div class="stat-value">{{ stats.groupCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">采集变量</div><div class="stat-value">{{ stats.variableCount || 0 }}</div></el-card>
			</el-col>
			<el-col :xs="12" :sm="8" :md="4">
				<el-card shadow="never"><div class="stat-label">驱动数量</div><div class="stat-value">{{ stats.driverCount || 0 }}</div></el-card>
			</el-col>
		</el-row>

		<el-card shadow="never">
			<el-form :inline="true" :model="query" class="iot-search">
				<el-form-item label="设备名称">
					<el-input v-model="query.keyword" placeholder="请输入设备名称" clearable @keyup.enter="loadData" />
				</el-form-item>
				<el-form-item label="驱动">
					<el-select v-model="query.driverId" placeholder="全部" clearable style="width: 180px">
						<el-option v-for="item in drivers" :key="item.id" :label="item.driverName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item>
					<el-button type="primary" @click="loadData">查询</el-button>
					<el-button @click="resetQuery">重置</el-button>
					<el-button type="success" @click="openAdd">新增设备</el-button>
					<el-button @click="exportBackup">导出备份</el-button>
					<el-upload :auto-upload="false" :show-file-list="false" accept=".xlsx" :on-change="importBackup" style="display: inline-block; margin-left: 12px">
						<el-button>导入备份</el-button>
					</el-upload>
					<el-button type="warning" :disabled="!selectedIds.length" style="margin-left: 12px" @click="openBatchEdit">批量修改</el-button>
					<el-button type="danger" :disabled="!selectedIds.length" @click="batchRemove">批量删除</el-button>
				</el-form-item>
			</el-form>

			<el-table v-loading="loading" :data="tableData" border stripe @selection-change="onSelectionChange">
				<el-table-column type="selection" width="46" />
				<el-table-column prop="deviceName" label="设备名称" min-width="150" />
				<el-table-column prop="index" label="排序" width="80" />
				<el-table-column prop="driverName" label="驱动" min-width="130" />
				<el-table-column label="类型" width="90">
					<template #default="{ row }">
						<el-tag :type="row.deviceTypeEnum === 0 ? 'info' : 'success'">{{ row.deviceTypeEnum === 0 ? '分组' : '设备' }}</el-tag>
					</template>
				</el-table-column>
				<el-table-column label="启动" width="80">
					<template #default="{ row }">{{ row.autoStart ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column label="变化上传" width="100">
					<template #default="{ row }">{{ row.cgUpload ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column prop="enforcePeriod" label="归档周期(ms)" width="130" />
				<el-table-column prop="cmdPeriod" label="指令间隔(ms)" width="130" />
				<el-table-column prop="description" label="描述" min-width="140" show-overflow-tooltip />
				<el-table-column label="操作" width="330" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openEdit(row)">编辑</el-button>
						<el-button link type="primary" @click="openDetail(row)">详情</el-button>
						<el-button v-if="row.deviceTypeEnum !== 0" link type="primary" @click="copyDevice(row)">复制</el-button>
						<el-button v-if="row.deviceTypeEnum !== 0" link type="primary" @click="requestAttributes(row)">获取属性</el-button>
						<el-button link type="danger" @click="remove(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>

			<el-pagination
				class="iot-pager"
				v-model:current-page="query.page"
				v-model:page-size="query.pageSize"
				:total="total"
				:page-sizes="[10, 20, 50, 100]"
				layout="total, sizes, prev, pager, next, jumper"
				@current-change="loadData"
				@size-change="loadData"
			/>
		</el-card>

		<el-dialog v-model="dialogVisible" :title="form.id ? '编辑设备' : '新增设备'" width="620px" destroy-on-close>
			<el-form :model="form" label-width="110px">
				<el-form-item label="设备名称" required>
					<el-input v-model="form.deviceName" placeholder="请输入设备名称" />
				</el-form-item>
				<el-form-item label="类型">
					<el-select v-model="form.deviceTypeEnum" style="width: 100%">
						<el-option label="设备" :value="1" />
						<el-option label="分组" :value="0" />
					</el-select>
				</el-form-item>
				<el-form-item label="驱动">
					<el-select v-model="form.driverId" placeholder="请选择驱动" clearable style="width: 100%">
						<el-option v-for="item in drivers" :key="item.id" :label="item.driverName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="父级分组">
					<el-select v-model="form.parentId" placeholder="顶级" clearable style="width: 100%">
						<el-option v-for="item in groups" :key="item.id" :label="item.deviceName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="排序">
					<el-input v-model.number="form.index" type="number" />
				</el-form-item>
				<el-form-item label="归档周期(ms)">
					<el-input v-model.number="form.enforcePeriod" type="number" />
				</el-form-item>
				<el-form-item label="指令间隔(ms)">
					<el-input v-model.number="form.cmdPeriod" type="number" />
				</el-form-item>
				<el-form-item label="自动启动">
					<el-switch v-model="form.autoStart" />
				</el-form-item>
				<el-form-item label="变化上传">
					<el-switch v-model="form.cgUpload" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="form.description" type="textarea" :rows="3" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="dialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="saving" @click="save">保存</el-button>
			</template>
		</el-dialog>

		<el-drawer v-model="detailVisible" title="设备详情" size="80%">
			<el-descriptions :column="2" border>
				<el-descriptions-item label="设备名称">{{ detail.deviceName }}</el-descriptions-item>
				<el-descriptions-item label="驱动">{{ detail.driverName }}</el-descriptions-item>
				<el-descriptions-item label="类型">{{ detail.deviceTypeEnum === 0 ? '分组' : '设备' }}</el-descriptions-item>
				<el-descriptions-item label="排序">{{ detail.index }}</el-descriptions-item>
				<el-descriptions-item label="描述" :span="2">{{ detail.description }}</el-descriptions-item>
			</el-descriptions>

			<el-divider content-position="left">通讯配置</el-divider>
			<div class="iot-section-toolbar">
				<el-button type="primary" size="small" @click="openConfigAdd">新增配置</el-button>
				<el-button type="warning" size="small" :disabled="!configSelection.length" @click="openConfigBatchEdit">批量修改</el-button>
				<el-button size="small" @click="exportConfigs">导出配置</el-button>
			</div>
			<el-table :data="detail.deviceConfigs || []" border size="small" @selection-change="onConfigSelectionChange">
				<el-table-column type="selection" width="42" />
				<el-table-column prop="deviceConfigName" label="名称" min-width="120" />
				<el-table-column label="属性侧" width="100">
					<template #default="{ row }">{{ dataSideLabel(row.dataSide) }}</template>
				</el-table-column>
				<el-table-column prop="value" label="值" min-width="150" />
				<el-table-column prop="description" label="描述" min-width="140" />
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openConfigEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="removeConfig(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>

			<el-divider content-position="left">变量</el-divider>
			<div class="iot-section-toolbar">
				<el-button size="small" @click="refreshDetail">刷新实时值</el-button>
				<el-button type="success" size="small" @click="openBatchWrite">批量写入</el-button>
				<el-button type="primary" size="small" @click="openVariableAdd">新增变量</el-button>
				<el-button type="warning" size="small" :disabled="!variableSelection.length" @click="openVariableBatchEdit">批量修改</el-button>
				<el-button size="small" @click="exportVariables">导出变量</el-button>
				<el-upload :auto-upload="false" :show-file-list="false" accept=".xlsx" :on-change="importVariables" style="display: inline-block; margin-left: 12px">
					<el-button size="small">导入变量</el-button>
				</el-upload>
			</div>
			<el-table :data="detail.deviceVariables || []" border size="small" @selection-change="onVariableSelectionChange">
				<el-table-column type="selection" width="42" />
				<el-table-column prop="name" label="变量名" min-width="120" />
				<el-table-column prop="alias" label="别名" min-width="100" />
				<el-table-column prop="deviceAddress" label="地址" min-width="110" />
				<el-table-column label="数据类型" width="100">
					<template #default="{ row }">{{ dataTypeLabel(row.dataType) }}</template>
				</el-table-column>
				<el-table-column label="权限" width="90">
					<template #default="{ row }">{{ protectLabel(row.protectType) }}</template>
				</el-table-column>
				<el-table-column prop="method" label="方法" min-width="120" show-overflow-tooltip />
				<el-table-column label="实时值" min-width="110" show-overflow-tooltip>
					<template #default="{ row }">{{ row.cookedValue ?? row.value ?? '-' }}</template>
				</el-table-column>
				<el-table-column label="状态" width="110">
					<template #default="{ row }"><el-tag :type="Number(row.statusType) === 0 ? 'success' : 'danger'">{{ statusLabel(row.statusType) }}</el-tag></template>
				</el-table-column>
				<el-table-column prop="timestamp" label="采集时间" min-width="155" />
				<el-table-column prop="message" label="状态信息" min-width="120" show-overflow-tooltip />
				<el-table-column label="上传" width="70">
					<template #default="{ row }">{{ row.isUpload ? '是' : '否' }}</template>
				</el-table-column>
				<el-table-column label="操作" width="130" fixed="right">
					<template #default="{ row }">
						<el-button link type="primary" @click="openVariableEdit(row)">编辑</el-button>
						<el-button link type="danger" @click="removeVariable(row)">删除</el-button>
					</template>
				</el-table-column>
			</el-table>
		</el-drawer>

		<el-dialog v-model="configDialogVisible" :title="configForm.id ? '编辑通讯配置' : '新增通讯配置'" width="560px" destroy-on-close>
			<el-form :model="configForm" label-width="110px">
				<el-form-item label="名称" required>
					<el-select v-if="driverConfigs.length" v-model="configForm.deviceConfigName" filterable style="width: 100%" @change="onConfigNameChange">
						<el-option v-for="item in driverConfigs" :key="item.name" :label="`${item.name} (${item.description})`" :value="item.name" />
					</el-select>
					<el-input v-else v-model="configForm.deviceConfigName" placeholder="如 IpAddress" />
				</el-form-item>
				<el-form-item label="属性侧">
					<el-select v-model="configForm.dataSide" style="width: 100%">
						<el-option label="任意侧" :value="0" />
						<el-option label="客户端" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="值">
					<el-input v-model="configForm.value" placeholder="请输入配置值" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="configForm.description" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="configDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="configSaving" @click="saveConfig">保存</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="writeDialogVisible" title="批量写入变量" width="620px" destroy-on-close>
			<el-alert title="仅列出读写和只写变量；留空的变量不会写入。" type="info" :closable="false" style="margin-bottom: 12px" />
			<el-table :data="writeRows" border size="small" max-height="420">
				<el-table-column prop="name" label="变量名" min-width="150" />
				<el-table-column label="写入值" min-width="220">
					<template #default="{ row }"><el-input v-model="row.value" placeholder="请输入写入值" /></template>
				</el-table-column>
				<el-table-column label="类型" width="110">
					<template #default="{ row }">{{ dataTypeLabel(row.dataType) }}</template>
				</el-table-column>
			</el-table>
			<template #footer>
				<el-button @click="writeDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="writing" @click="writeVariables">执行写入</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="variableDialogVisible" :title="variableForm.id ? '编辑变量' : '新增变量'" width="620px" destroy-on-close>
			<el-form :model="variableForm" label-width="110px">
				<el-form-item label="变量名" required>
					<el-input v-model="variableForm.name" placeholder="如 temperature" />
				</el-form-item>
				<el-form-item label="别名">
					<el-input v-model="variableForm.alias" placeholder="分组上传别名，可空" />
				</el-form-item>
				<el-form-item label="方法" required>
					<el-select v-model="variableForm.method" filterable style="width: 100%" @change="onMethodChange">
						<el-option v-for="item in methodOptions" :key="item.name" :label="`${item.displayName || item.name} (${item.name})`" :value="item.name" />
					</el-select>
				</el-form-item>
				<el-form-item label="地址">
					<el-input v-model="variableForm.deviceAddress" placeholder="如 0 或 40001" />
				</el-form-item>
				<el-form-item label="数据类型">
					<el-select v-model="variableForm.dataType" style="width: 100%">
						<el-option v-for="item in dataTypeOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="大小端">
					<el-select v-model="variableForm.endianType" style="width: 100%">
						<el-option v-for="item in endianOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="权限">
					<el-select v-model="variableForm.protectType" style="width: 100%">
						<el-option label="只读" :value="0" />
						<el-option label="读写" :value="1" />
						<el-option label="只写" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="表达式">
					<el-input v-model="variableForm.expressions" placeholder="如 temperature*0.1" />
				</el-form-item>
				<el-form-item label="排序">
					<el-input v-model.number="variableForm.index" type="number" />
				</el-form-item>
				<el-form-item label="上传">
					<el-switch v-model="variableForm.isUpload" />
				</el-form-item>
				<el-form-item label="触发">
					<el-switch v-model="variableForm.isTrigger" />
				</el-form-item>
				<el-form-item label="描述">
					<el-input v-model="variableForm.description" type="textarea" :rows="2" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="variableDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="variableSaving" @click="saveVariable">保存</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="batchDialogVisible" title="批量修改设备" width="580px" destroy-on-close>
			<el-alert title="仅勾选的字段会被修改，未勾选字段保持原值。" type="info" :closable="false" style="margin-bottom: 12px" />
			<el-form label-width="110px">
				<el-form-item label="驱动">
					<el-checkbox v-model="batchForm.useDriverId" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="batchForm.driverId" clearable :disabled="!batchForm.useDriverId" style="width: 260px">
						<el-option v-for="item in drivers" :key="item.id" :label="item.driverName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="父级分组">
					<el-checkbox v-model="batchForm.useParentId" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="batchForm.parentId" clearable :disabled="!batchForm.useParentId" style="width: 260px">
						<el-option v-for="item in groups" :key="item.id" :label="item.deviceName" :value="item.id" />
					</el-select>
				</el-form-item>
				<el-form-item label="启动">
					<el-checkbox v-model="batchForm.useAutoStart" style="margin-right: 8px">修改</el-checkbox>
					<el-switch v-model="batchForm.autoStart" :disabled="!batchForm.useAutoStart" />
				</el-form-item>
				<el-form-item label="变化上传">
					<el-checkbox v-model="batchForm.useCgUpload" style="margin-right: 8px">修改</el-checkbox>
					<el-switch v-model="batchForm.cgUpload" :disabled="!batchForm.useCgUpload" />
				</el-form-item>
				<el-form-item label="归档周期(ms)">
					<el-checkbox v-model="batchForm.useEnforcePeriod" style="margin-right: 8px">修改</el-checkbox>
					<el-input-number v-model="batchForm.enforcePeriod" :min="0" :disabled="!batchForm.useEnforcePeriod" />
				</el-form-item>
				<el-form-item label="指令间隔(ms)">
					<el-checkbox v-model="batchForm.useCmdPeriod" style="margin-right: 8px">修改</el-checkbox>
					<el-input-number v-model="batchForm.cmdPeriod" :min="0" :disabled="!batchForm.useCmdPeriod" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="batchDialogVisible = false">取消</el-button>
				<el-button type="primary" :loading="batchSaving" @click="submitBatchEdit">保存</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="configBatchVisible" title="批量修改通讯配置" width="540px" destroy-on-close>
			<el-alert title="仅勾选的字段会被修改。" type="info" :closable="false" style="margin-bottom: 12px" />
			<el-form label-width="100px">
				<el-form-item label="属性侧">
					<el-checkbox v-model="configBatchForm.useDataSide" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="configBatchForm.dataSide" :disabled="!configBatchForm.useDataSide" style="width: 220px">
						<el-option label="任意侧" :value="0" />
						<el-option label="客户端" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="值">
					<el-checkbox v-model="configBatchForm.useValue" style="margin-right: 8px">修改</el-checkbox>
					<el-input v-model="configBatchForm.value" :disabled="!configBatchForm.useValue" style="width: 260px" />
				</el-form-item>
				<el-form-item label="描述">
					<el-checkbox v-model="configBatchForm.useDescription" style="margin-right: 8px">修改</el-checkbox>
					<el-input v-model="configBatchForm.description" :disabled="!configBatchForm.useDescription" style="width: 260px" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="configBatchVisible = false">取消</el-button>
				<el-button type="primary" :loading="configBatchSaving" @click="submitConfigBatchEdit">保存</el-button>
			</template>
		</el-dialog>

		<el-dialog v-model="variableBatchVisible" title="批量修改变量" width="580px" destroy-on-close>
			<el-alert title="仅勾选的字段会被修改。" type="info" :closable="false" style="margin-bottom: 12px" />
			<el-form label-width="100px">
				<el-form-item label="方法">
					<el-checkbox v-model="variableBatchForm.useMethod" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="variableBatchForm.method" filterable :disabled="!variableBatchForm.useMethod" style="width: 300px">
						<el-option v-for="item in methodOptions" :key="item.name" :label="`${item.displayName || item.name} (${item.name})`" :value="item.name" />
					</el-select>
				</el-form-item>
				<el-form-item label="数据类型">
					<el-checkbox v-model="variableBatchForm.useDataType" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="variableBatchForm.dataType" :disabled="!variableBatchForm.useDataType" style="width: 220px">
						<el-option v-for="item in dataTypeOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="大小端">
					<el-checkbox v-model="variableBatchForm.useEndianType" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="variableBatchForm.endianType" :disabled="!variableBatchForm.useEndianType" style="width: 220px">
						<el-option v-for="item in endianOptions" :key="item.value" :label="item.label" :value="item.value" />
					</el-select>
				</el-form-item>
				<el-form-item label="权限">
					<el-checkbox v-model="variableBatchForm.useProtectType" style="margin-right: 8px">修改</el-checkbox>
					<el-select v-model="variableBatchForm.protectType" :disabled="!variableBatchForm.useProtectType" style="width: 220px">
						<el-option label="只读" :value="0" />
						<el-option label="读写" :value="1" />
						<el-option label="只写" :value="2" />
					</el-select>
				</el-form-item>
				<el-form-item label="上传">
					<el-checkbox v-model="variableBatchForm.useIsUpload" style="margin-right: 8px">修改</el-checkbox>
					<el-switch v-model="variableBatchForm.isUpload" :disabled="!variableBatchForm.useIsUpload" />
				</el-form-item>
				<el-form-item label="触发">
					<el-checkbox v-model="variableBatchForm.useIsTrigger" style="margin-right: 8px">修改</el-checkbox>
					<el-switch v-model="variableBatchForm.isTrigger" :disabled="!variableBatchForm.useIsTrigger" />
				</el-form-item>
				<el-form-item label="排序">
					<el-checkbox v-model="variableBatchForm.useIndex" style="margin-right: 8px">修改</el-checkbox>
					<el-input-number v-model="variableBatchForm.index" :disabled="!variableBatchForm.useIndex" />
				</el-form-item>
			</el-form>
			<template #footer>
				<el-button @click="variableBatchVisible = false">取消</el-button>
				<el-button type="primary" :loading="variableBatchSaving" @click="submitVariableBatchEdit">保存</el-button>
			</template>
		</el-dialog>
	</div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { iotDeviceApi, iotDeviceConfigApi, iotDeviceVariableApi, iotDriverApi } from '/@/api/iot';

const loading = ref(false);
const saving = ref(false);
const tableData = ref([]);
const drivers = ref([]);
const groups = ref([]);
const total = ref(0);
const stats = ref({});
const dialogVisible = ref(false);
const detailVisible = ref(false);
const detail = ref({});
const writeDialogVisible = ref(false);
const writing = ref(false);
const writeRows = ref([]);

const selectedIds = ref([]);
const batchDialogVisible = ref(false);
const batchSaving = ref(false);
const batchForm = reactive({
	useDriverId: false, driverId: null,
	useParentId: false, parentId: null,
	useAutoStart: false, autoStart: true,
	useCgUpload: false, cgUpload: false,
	useEnforcePeriod: false, enforcePeriod: 0,
	useCmdPeriod: false, cmdPeriod: 1000,
});

const configSelection = ref([]);
const configBatchVisible = ref(false);
const configBatchSaving = ref(false);
const configBatchForm = reactive({ useDataSide: false, dataSide: 0, useValue: false, value: '', useDescription: false, description: '' });

const variableSelection = ref([]);
const variableBatchVisible = ref(false);
const variableBatchSaving = ref(false);
const variableBatchForm = reactive({
	useMethod: false, method: '',
	useDataType: false, dataType: 9,
	useEndianType: false, endianType: 2,
	useProtectType: false, protectType: 0,
	useIsUpload: false, isUpload: true,
	useIsTrigger: false, isTrigger: false,
	useIndex: false, index: 0,
});

const downloadBlob = (data, filename) => {
	const blob = new Blob([data], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
	const url = URL.createObjectURL(blob);
	const link = document.createElement('a');
	link.href = url;
	link.download = filename;
	link.click();
	URL.revokeObjectURL(url);
};

const configDialogVisible = ref(false);
const configSaving = ref(false);
const driverConfigs = ref([]);
const configForm = reactive({ id: 0, deviceId: 0, deviceConfigName: '', dataSide: 0, value: '', description: '', enumInfo: '' });

const variableDialogVisible = ref(false);
const variableSaving = ref(false);
const methodOptions = ref([]);
const variableForm = reactive({
	id: 0,
	deviceId: 0,
	name: '',
	alias: '',
	method: '',
	deviceAddress: '',
	dataType: 9,
	endianType: 2,
	expressions: '',
	isUpload: true,
	isTrigger: false,
	protectType: 0,
	index: 0,
	description: '',
});

const dataTypeOptions = [
	{ value: 0, label: 'bit' }, { value: 1, label: 'bool' }, { value: 2, label: 'uint8' }, { value: 3, label: 'int8' },
	{ value: 4, label: 'uint16' }, { value: 5, label: 'int16' }, { value: 6, label: 'bcd16' }, { value: 7, label: 'uint32' },
	{ value: 8, label: 'int32' }, { value: 9, label: 'float' }, { value: 10, label: 'bcd32' }, { value: 11, label: 'uint64' },
	{ value: 12, label: 'int64' }, { value: 13, label: 'double' }, { value: 14, label: 'ascii' }, { value: 15, label: 'utf8' },
	{ value: 16, label: 'datetime' }, { value: 17, label: 'timestamp(ms)' }, { value: 18, label: 'timestamp(s)' }, { value: 19, label: 'Any' },
	{ value: 25, label: 'Gb2312' },
];
const endianOptions = [
	{ value: 0, label: 'None' }, { value: 1, label: 'BigEndian' }, { value: 2, label: 'LittleEndian' },
	{ value: 3, label: 'BigEndianSwap' }, { value: 4, label: 'LittleEndianSwap' },
];

const dataSideLabel = (v) => (Number(v) === 2 ? '客户端' : '任意侧');
const dataTypeLabel = (v) => dataTypeOptions.find((x) => x.value === Number(v))?.label ?? v;
const protectLabel = (v) => ['只读', '读写', '只写'][Number(v)] ?? v;
const statusLabel = (v) => ({
	0: '正常', 1: '地址错误', 2: '方法错误', 3: '表达式错误', 4: '异常', 5: '未采集',
	6: '自定义1', 7: '自定义2', 8: '自定义3', 9: '自定义4', 10: '自定义5',
})[Number(v)] ?? v;

const query = reactive({ keyword: '', driverId: null, page: 1, pageSize: 20 });
const form = reactive({
	id: 0,
	deviceName: '',
	deviceTypeEnum: 1,
	driverId: null,
	index: 0,
	enforcePeriod: 0,
	cmdPeriod: 1000,
	autoStart: true,
	cgUpload: false,
	description: '',
	parentId: null,
});

const loadStats = async () => {
	try {
		const res = await iotDeviceApi.stats();
		stats.value = res.data.result || {};
	} catch {
		stats.value = {};
	}
};

const loadData = async () => {
	loading.value = true;
	try {
		const res = await iotDeviceApi.page({ ...query });
		tableData.value = res.data.result.items || [];
		total.value = res.data.result.total || 0;
	} finally {
		loading.value = false;
	}
};

const loadDrivers = async () => {
	const res = await iotDeviceApi.driverList();
	drivers.value = res.data.result || [];
};

const loadGroups = async () => {
	const res = await iotDeviceApi.page({ keyword: '', page: 1, pageSize: 500 });
	groups.value = (res.data.result.items || []).filter((x) => x.deviceTypeEnum === 0);
};

const resetQuery = () => {
	query.keyword = '';
	query.driverId = null;
	query.page = 1;
	loadData();
};

const resetForm = () => {
	Object.assign(form, {
		id: 0,
		deviceName: '',
		deviceTypeEnum: 1,
		driverId: null,
		index: 0,
		enforcePeriod: 0,
		cmdPeriod: 1000,
		autoStart: true,
		cgUpload: false,
		description: '',
		parentId: null,
	});
};

const openAdd = () => {
	resetForm();
	dialogVisible.value = true;
};

const openEdit = (row) => {
	Object.assign(form, {
		id: row.id,
		deviceName: row.deviceName,
		deviceTypeEnum: row.deviceTypeEnum,
		driverId: row.driverId,
		index: row.index,
		enforcePeriod: row.enforcePeriod,
		cmdPeriod: row.cmdPeriod,
		autoStart: row.autoStart,
		cgUpload: row.cgUpload,
		description: row.description,
		parentId: row.parentId,
	});
	dialogVisible.value = true;
};

const save = async () => {
	if (!form.deviceName) {
		ElMessage.warning('请输入设备名称');
		return;
	}
	saving.value = true;
	try {
		if (form.id) await iotDeviceApi.update({ ...form });
		else await iotDeviceApi.add({ ...form });
		ElMessage.success('保存成功');
		dialogVisible.value = false;
		await loadData();
		await loadGroups();
		await loadStats();
	} finally {
		saving.value = false;
	}
};

const remove = async (row) => {
	await ElMessageBox.confirm(`确认删除设备「${row.deviceName}」吗？`, '提示', { type: 'warning' });
	await iotDeviceApi.delete(row.id);
	ElMessage.success('删除成功');
	await loadData();
	await loadGroups();
	await loadStats();
};

const copyDevice = async (row) => {
	const { value } = await ElMessageBox.prompt(`将复制「${row.deviceName}」及其通讯配置和变量，新设备默认不启动。`, '复制设备', {
		inputValue: '1',
		inputPattern: /^([1-9]|[1-9]\d|100)$/,
		inputErrorMessage: '请输入 1-100 的整数',
	});
	await iotDeviceApi.copy({ id: row.id, count: Number(value) });
	ElMessage.success('复制成功');
	await loadData();
	await loadStats();
};

const requestAttributes = async (row) => {
	await iotDeviceApi.requestAttributes(row.id);
	ElMessage.success('属性请求已发送');
};

const exportBackup = async () => {
	const res = await iotDeviceApi.export();
	const blob = new Blob([res.data], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
	const url = URL.createObjectURL(blob);
	const link = document.createElement('a');
	link.href = url;
	link.download = `iotgateway_backup_${Date.now()}.xlsx`;
	link.click();
	URL.revokeObjectURL(url);
};

const importBackup = async (uploadFile) => {
	const file = uploadFile.raw;
	if (!file) return;
	await ElMessageBox.confirm('导入将覆盖现有设备、变量、通讯配置和 RPC 日志，是否继续？', '覆盖恢复', { type: 'warning' });
	const res = await iotDeviceApi.importBackup(file);
	const result = res.data.result || {};
	ElMessage.success(`恢复成功：${result.deviceCount || 0} 个设备，${result.groupCount || 0} 个分组`);
	await loadData();
	await loadGroups();
	await loadStats();
};

const onSelectionChange = (rows) => {
	selectedIds.value = rows.map((x) => x.id);
};

const openBatchEdit = () => {
	Object.assign(batchForm, {
		useDriverId: false, driverId: null,
		useParentId: false, parentId: null,
		useAutoStart: false, autoStart: true,
		useCgUpload: false, cgUpload: false,
		useEnforcePeriod: false, enforcePeriod: 0,
		useCmdPeriod: false, cmdPeriod: 1000,
	});
	batchDialogVisible.value = true;
};

const submitBatchEdit = async () => {
	const data = { ids: selectedIds.value };
	if (batchForm.useDriverId && batchForm.driverId) data.driverId = batchForm.driverId;
	if (batchForm.useParentId) data.parentId = batchForm.parentId || 0;
	if (batchForm.useAutoStart) data.autoStart = batchForm.autoStart;
	if (batchForm.useCgUpload) data.cgUpload = batchForm.cgUpload;
	if (batchForm.useEnforcePeriod) data.enforcePeriod = batchForm.enforcePeriod;
	if (batchForm.useCmdPeriod) data.cmdPeriod = batchForm.cmdPeriod;
	if (Object.keys(data).length === 1) {
		ElMessage.warning('请至少勾选一个要修改的字段');
		return;
	}
	batchSaving.value = true;
	try {
		await iotDeviceApi.batchUpdate(data);
		ElMessage.success('批量修改成功');
		batchDialogVisible.value = false;
		await loadData();
		await loadStats();
	} finally {
		batchSaving.value = false;
	}
};

const batchRemove = async () => {
	await ElMessageBox.confirm(`确认删除选中的 ${selectedIds.value.length} 个设备吗？其变量与通讯配置将一并删除。`, '批量删除', { type: 'warning' });
	await iotDeviceApi.batchDelete(selectedIds.value);
	ElMessage.success('删除成功');
	await loadData();
	await loadGroups();
	await loadStats();
};

const refreshDetail = async () => {
	if (!detail.value.id) return;
	const res = await iotDeviceApi.detail(detail.value.id);
	detail.value = res.data.result || {};
};

const loadDriverMeta = async () => {
	const driver = drivers.value.find((x) => x.id === detail.value.driverId);
	driverConfigs.value = [];
	methodOptions.value = [];
	if (!driver?.assembleName) return;

	try {
		const [configRes, methodRes] = await Promise.all([
			iotDriverApi.driverConfigs(driver.assembleName),
			iotDriverApi.driverMethods(driver.assembleName),
		]);
		driverConfigs.value = configRes.data.result || [];
		methodOptions.value = methodRes.data.result || [];
	} catch {
		driverConfigs.value = [];
		methodOptions.value = [];
	}
};

const openDetail = async (row) => {
	const res = await iotDeviceApi.detail(row.id);
	detail.value = res.data.result || {};
	detailVisible.value = true;
	await loadDriverMeta();
};

const openConfigAdd = () => {
	Object.assign(configForm, { id: 0, deviceId: detail.value.id, deviceConfigName: '', dataSide: 0, value: '', description: '', enumInfo: '' });
	configDialogVisible.value = true;
};

const openConfigEdit = (row) => {
	Object.assign(configForm, {
		id: row.id,
		deviceId: row.deviceId,
		deviceConfigName: row.deviceConfigName,
		dataSide: Number(row.dataSide),
		value: row.value,
		description: row.description,
		enumInfo: row.enumInfo,
	});
	configDialogVisible.value = true;
};

const onConfigNameChange = (name) => {
	const matched = driverConfigs.value.find((x) => x.name === name);
	if (matched && !configForm.description) configForm.description = matched.description;
	if (matched && !configForm.id) {
		configForm.value = matched.defaultValue ?? '';
		configForm.enumInfo = matched.enumInfo ?? '';
	}
};

const saveConfig = async () => {
	if (!configForm.deviceConfigName) {
		ElMessage.warning('请输入配置名称');
		return;
	}
	configSaving.value = true;
	try {
		if (configForm.id) await iotDeviceConfigApi.update({ ...configForm });
		else await iotDeviceConfigApi.add({ ...configForm });
		ElMessage.success('保存成功');
		configDialogVisible.value = false;
		await refreshDetail();
	} finally {
		configSaving.value = false;
	}
};

const removeConfig = async (row) => {
	await ElMessageBox.confirm(`确认删除配置「${row.deviceConfigName}」吗？`, '提示', { type: 'warning' });
	await iotDeviceConfigApi.delete(row.id);
	ElMessage.success('删除成功');
	await refreshDetail();
};

const openVariableAdd = () => {
	Object.assign(variableForm, {
		id: 0,
		deviceId: detail.value.id,
		name: '',
		alias: '',
		method: '',
		deviceAddress: '',
		dataType: 9,
		endianType: 2,
		expressions: '',
		isUpload: true,
		isTrigger: false,
		protectType: 0,
		index: 0,
		description: '',
	});
	variableDialogVisible.value = true;
};

const openVariableEdit = (row) => {
	Object.assign(variableForm, {
		id: row.id,
		deviceId: row.deviceId,
		name: row.name,
		alias: row.alias,
		method: row.method,
		deviceAddress: row.deviceAddress,
		dataType: Number(row.dataType),
		endianType: Number(row.endianType),
		expressions: row.expressions,
		isUpload: row.isUpload,
		isTrigger: row.isTrigger,
		protectType: Number(row.protectType),
		index: row.index,
		description: row.description,
	});
	variableDialogVisible.value = true;
};

const onMethodChange = (name) => {
	const matched = methodOptions.value.find((x) => x.name === name);
	if (matched) variableForm.protectType = matched.protectType;
};

const saveVariable = async () => {
	if (!variableForm.name) {
		ElMessage.warning('请输入变量名');
		return;
	}
	if (!variableForm.method) {
		ElMessage.warning('请选择方法');
		return;
	}
	variableSaving.value = true;
	try {
		if (variableForm.id) await iotDeviceVariableApi.update({ ...variableForm });
		else await iotDeviceVariableApi.add({ ...variableForm });
		ElMessage.success('保存成功');
		variableDialogVisible.value = false;
		await refreshDetail();
	} finally {
		variableSaving.value = false;
	}
};

const removeVariable = async (row) => {
	await ElMessageBox.confirm(`确认删除变量「${row.name}」吗？`, '提示', { type: 'warning' });
	await iotDeviceVariableApi.delete(row.id);
	ElMessage.success('删除成功');
	await refreshDetail();
};

const onConfigSelectionChange = (rows) => {
	configSelection.value = rows.map((x) => x.id);
};

const openConfigBatchEdit = () => {
	Object.assign(configBatchForm, { useDataSide: false, dataSide: 0, useValue: false, value: '', useDescription: false, description: '' });
	configBatchVisible.value = true;
};

const submitConfigBatchEdit = async () => {
	const data = { ids: configSelection.value };
	if (configBatchForm.useDataSide) data.dataSide = configBatchForm.dataSide;
	if (configBatchForm.useValue) data.value = configBatchForm.value;
	if (configBatchForm.useDescription) data.description = configBatchForm.description;
	if (Object.keys(data).length === 1) {
		ElMessage.warning('请至少勾选一个要修改的字段');
		return;
	}
	configBatchSaving.value = true;
	try {
		await iotDeviceConfigApi.batchUpdate(data);
		ElMessage.success('批量修改成功');
		configBatchVisible.value = false;
		await refreshDetail();
	} finally {
		configBatchSaving.value = false;
	}
};

const exportConfigs = async () => {
	const res = await iotDeviceConfigApi.export(detail.value.id);
	downloadBlob(res.data, `device_configs_${detail.value.id}_${Date.now()}.xlsx`);
};

const onVariableSelectionChange = (rows) => {
	variableSelection.value = rows.map((x) => x.id);
};

const openVariableBatchEdit = () => {
	Object.assign(variableBatchForm, {
		useMethod: false, method: '',
		useDataType: false, dataType: 9,
		useEndianType: false, endianType: 2,
		useProtectType: false, protectType: 0,
		useIsUpload: false, isUpload: true,
		useIsTrigger: false, isTrigger: false,
		useIndex: false, index: 0,
	});
	variableBatchVisible.value = true;
};

const submitVariableBatchEdit = async () => {
	const data = { ids: variableSelection.value };
	if (variableBatchForm.useMethod && variableBatchForm.method) data.method = variableBatchForm.method;
	if (variableBatchForm.useDataType) data.dataType = variableBatchForm.dataType;
	if (variableBatchForm.useEndianType) data.endianType = variableBatchForm.endianType;
	if (variableBatchForm.useProtectType) data.protectType = variableBatchForm.protectType;
	if (variableBatchForm.useIsUpload) data.isUpload = variableBatchForm.isUpload;
	if (variableBatchForm.useIsTrigger) data.isTrigger = variableBatchForm.isTrigger;
	if (variableBatchForm.useIndex) data.index = variableBatchForm.index;
	if (Object.keys(data).length === 1) {
		ElMessage.warning('请至少勾选一个要修改的字段');
		return;
	}
	variableBatchSaving.value = true;
	try {
		await iotDeviceVariableApi.batchUpdate(data);
		ElMessage.success('批量修改成功');
		variableBatchVisible.value = false;
		await refreshDetail();
	} finally {
		variableBatchSaving.value = false;
	}
};

const exportVariables = async () => {
	const res = await iotDeviceVariableApi.export(detail.value.id);
	downloadBlob(res.data, `device_variables_${detail.value.id}_${Date.now()}.xlsx`);
};

const importVariables = async (uploadFile) => {
	const file = uploadFile.raw;
	if (!file) return;
	const res = await iotDeviceVariableApi.import(file, detail.value.id);
	const result = res.data.result || {};
	ElMessage.success(`导入完成：新增 ${result.insertCount || 0}，更新 ${result.updateCount || 0}`);
	await refreshDetail();
};

const openBatchWrite = () => {
	writeRows.value = (detail.value.deviceVariables || [])
		.filter((x) => Number(x.protectType) !== 0)
		.map((x) => ({ name: x.name, dataType: Number(x.dataType), value: '' }));
	if (!writeRows.value.length) {
		ElMessage.warning('当前设备没有可写变量');
		return;
	}
	writeDialogVisible.value = true;
};

const writeVariables = async () => {
	const values = {};
	for (const row of writeRows.value.filter((x) => String(x.value).trim() !== '')) {
		if ([0, 1].includes(row.dataType)) {
			values[row.name] = ['true', '1', 'yes', '是'].includes(String(row.value).toLowerCase());
		} else if ((row.dataType >= 2 && row.dataType <= 13) || [17, 18].includes(row.dataType)) {
			const number = Number(row.value);
			if (!Number.isFinite(number)) {
				ElMessage.warning(`变量 ${row.name} 的写入值不是有效数字`);
				return;
			}
			values[row.name] = number;
		} else {
			values[row.name] = row.value;
		}
	}
	if (!Object.keys(values).length) {
		ElMessage.warning('请至少填写一个写入值');
		return;
	}
	writing.value = true;
	try {
		const res = await iotDeviceVariableApi.write({ deviceId: detail.value.id, values });
		const result = res.data.result || {};
		if (!result.isSuccess) {
			ElMessage.error(result.description || '写入失败');
			return;
		}
		ElMessage.success('写入成功');
		writeDialogVisible.value = false;
		await refreshDetail();
	} finally {
		writing.value = false;
	}
};

onMounted(async () => {
	await loadDrivers();
	await loadGroups();
	await loadData();
	await loadStats();
});
</script>

<style scoped>
.iot-page {
	padding: 16px;
}
.iot-stats {
	margin-bottom: 12px;
}
.iot-stats .el-card {
	margin-bottom: 8px;
}
.stat-label {
	font-size: 13px;
	color: var(--el-text-color-secondary);
}
.stat-value {
	margin-top: 6px;
	font-size: 22px;
	font-weight: 600;
}
.stat-value.online {
	color: var(--el-color-success);
}
.stat-value.offline {
	color: var(--el-color-info);
}
.iot-search {
	margin-bottom: 8px;
}
.iot-pager {
	margin-top: 16px;
	justify-content: flex-end;
}
.iot-section-toolbar {
	margin-bottom: 8px;
	text-align: right;
}
</style>
