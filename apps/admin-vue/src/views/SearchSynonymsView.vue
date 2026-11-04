<template>
  <div>
    <div class="head">
      <div>
        <h2 class="head__title">商品搜索同义词</h2>
        <p class="head__desc">规则在查询时展开，保存后立即生效，不重建商品索引</p>
      </div>
      <div class="head__actions">
        <el-button @click="openPreview">查询预览</el-button>
        <el-button type="primary" @click="openCreate">新增规则</el-button>
      </div>
    </div>

    <section class="panel panel--filters">
      <el-input
        v-model="filters.keyword"
        class="filter__keyword"
        placeholder="搜索词条或备注"
        clearable
      >
        <template #prefix><el-icon><Search /></el-icon></template>
      </el-input>
      <el-select v-model="filters.ruleType" class="filter__select" aria-label="规则类型">
        <el-option label="全部类型" :value="0" />
        <el-option v-for="item in RULE_TYPES" :key="item.value" :label="item.label" :value="item.value" />
      </el-select>
      <el-select v-model="filters.scopeType" class="filter__select" aria-label="作用域">
        <el-option label="全部作用域" :value="0" />
        <el-option v-for="item in SCOPE_TYPES" :key="item.value" :label="item.label" :value="item.value" />
      </el-select>
      <el-select v-model="filters.status" class="filter__select" aria-label="状态">
        <el-option label="全部状态" :value="-1" />
        <el-option label="启用" :value="1" />
        <el-option label="停用" :value="0" />
      </el-select>
      <span class="filter__count">{{ filteredRows.length }} / {{ rows.length }} 条</span>
    </section>

    <section class="panel panel--table">
      <el-table
        v-loading="loading"
        :data="pagedRows"
        row-key="id"
        max-height="610"
        empty-text="暂无同义词规则"
      >
        <el-table-column label="规则" min-width="310">
          <template #default="{ row }">
            <div class="rule-terms">{{ ruleDisplay(row) }}</div>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="112">
          <template #default="{ row }">
            <el-tag :type="ruleTagType(row.ruleType)" effect="plain" round>
              {{ row.ruleTypeName }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="作用域" min-width="150">
          <template #default="{ row }">
            <span class="scope-name">{{ scopeLabel(row) }}</span>
            <span v-if="row.scopeType !== 1" class="scope-id">#{{ row.scopeId }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="priority" label="优先级" width="82" align="right" />
        <el-table-column label="状态" width="88">
          <template #default="{ row }">
            <el-switch
              v-model="row.status"
              :active-value="1"
              :inactive-value="0"
              @change="toggleStatus(row)"
            />
          </template>
        </el-table-column>
        <el-table-column label="备注" min-width="150">
          <template #default="{ row }">{{ emptyText(row.remark) }}</template>
        </el-table-column>
        <el-table-column label="更新时间" width="145">
          <template #default="{ row }">{{ formatDateTime(row.updatedAt || row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          :total="filteredRows.length"
          layout="total, sizes, prev, pager, next"
        />
      </div>
    </section>

    <el-dialog
      v-model="editorOpen"
      :title="editingId ? '编辑同义词规则' : '新增同义词规则'"
      width="560px"
      destroy-on-close
    >
      <el-form label-position="top" class="editor" @submit.prevent>
        <div class="editor__row">
          <el-form-item label="规则类型" class="editor__half">
            <el-select v-model="form.ruleType" @change="onRuleTypeChange">
              <el-option
                v-for="item in RULE_TYPES"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="作用域" class="editor__half">
            <el-select v-model="form.scopeType" @change="onScopeTypeChange">
              <el-option
                v-for="item in SCOPE_TYPES"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
        </div>

        <el-form-item v-if="form.scopeType !== 1" label="作用域对象">
          <el-select
            v-model="form.scopeId"
            filterable
            placeholder="请选择平台 / 分类 / 品牌"
            :loading="optionsLoading"
          >
            <el-option
              v-for="item in scopeOptions"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="词条">
          <el-input
            v-model="form.terms"
            type="textarea"
            :rows="5"
            placeholder="睫毛,眼睫毛"
            spellcheck="false"
          />
          <p class="editor__help">{{ termsHelp }}</p>
        </el-form-item>

        <div class="editor__row">
          <el-form-item label="优先级" class="editor__half">
            <el-input-number v-model="form.priority" :min="0" :max="9999" controls-position="right" />
          </el-form-item>
          <el-form-item label="状态" class="editor__half">
            <el-switch
              v-model="form.status"
              :active-value="1"
              :inactive-value="0"
              active-text="启用"
              inactive-text="停用"
            />
          </el-form-item>
        </div>

        <el-form-item label="备注">
          <el-input v-model="form.remark" maxlength="256" show-word-limit placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editorOpen = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="previewOpen" title="关键词扩展预览" width="720px" destroy-on-close>
      <div class="preview__form">
        <el-input
          v-model="preview.keyword"
          class="preview__keyword"
          placeholder="输入用户可能搜索的关键词"
          @keyup.enter="runPreview"
        />
        <el-button type="primary" :loading="previewLoading" @click="runPreview">预览</el-button>
      </div>
      <div class="preview__scope">
        <el-select v-model="preview.platformId" clearable placeholder="平台（可选）">
          <el-option v-for="item in platforms" :key="item.id" :label="item.name" :value="item.id" />
        </el-select>
        <el-select v-model="preview.categoryId" clearable filterable placeholder="分类（可选）">
          <el-option v-for="item in categories" :key="item.id" :label="item.name" :value="item.id" />
        </el-select>
        <el-select v-model="preview.brandId" clearable filterable placeholder="品牌（可选）">
          <el-option v-for="item in brands" :key="item.id" :label="item.name" :value="item.id" />
        </el-select>
      </div>
      <el-table v-loading="previewLoading" :data="previewRows" max-height="360">
        <el-table-column prop="text" label="实际查询词" min-width="180" />
        <el-table-column label="权重" width="80" align="right">
          <template #default="{ row }">{{ row.boost.toFixed(2) }}</template>
        </el-table-column>
        <el-table-column prop="ruleTypeName" label="来源" width="110" />
        <el-table-column label="命中词" min-width="120">
          <template #default="{ row }">{{ emptyText(row.sourceTerm) }}</template>
        </el-table-column>
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { Search } from '@element-plus/icons-vue';
import request from '@/api/request';
import { emptyText, formatDateTime } from '@/utils/format';

const RULE_TYPES = [
  { value: 1, label: '等价词' },
  { value: 2, label: '单向映射' },
  { value: 3, label: '错别字纠正' },
  { value: 4, label: '类目扩展' },
];
const SCOPE_TYPES = [
  { value: 1, label: '全局' },
  { value: 2, label: '平台' },
  { value: 3, label: '分类' },
  { value: 4, label: '品牌' },
];

const rows = ref<any[]>([]);
const loading = ref(false);
const saving = ref(false);
const optionsLoading = ref(false);
const platforms = ref<any[]>([]);
const categories = ref<any[]>([]);
const brands = ref<any[]>([]);

const filters = reactive({
  keyword: '',
  ruleType: 0,
  scopeType: 0,
  status: -1,
});

const filteredRows = computed(() => {
  const keyword = filters.keyword.trim().toLowerCase();
  return rows.value.filter((row) => {
    if (filters.ruleType > 0 && row.ruleType !== filters.ruleType) return false;
    if (filters.scopeType > 0 && row.scopeType !== filters.scopeType) return false;
    if (filters.status >= 0 && row.status !== filters.status) return false;
    if (!keyword) return true;
    return String(row.terms).toLowerCase().includes(keyword)
      || String(row.remark || '').toLowerCase().includes(keyword);
  });
});
const page = ref(1);
const pageSize = ref(20);
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return filteredRows.value.slice(start, start + pageSize.value);
});

const editorOpen = ref(false);
const editingId = ref('');
const form = reactive({
  terms: '',
  ruleType: 1,
  scopeType: 1,
  scopeId: '',
  priority: 100,
  status: 1,
  remark: '',
});

const termsHelp = computed(() => {
  if (form.ruleType === 1) return '英文逗号分隔。每个词都会与组内其他词双向等价。';
  if (form.ruleType === 4) return '第一个词是来源，其余词是以低权重参与召回的扩展词。';
  return '第一个词是来源，其余词是目标。只在命中来源时单向扩展。';
});

const scopeOptions = computed(() => {
  if (form.scopeType === 2) return platforms.value;
  if (form.scopeType === 3) return categories.value;
  if (form.scopeType === 4) return brands.value;
  return [];
});

const previewOpen = ref(false);
const previewLoading = ref(false);
const previewRows = ref<any[]>([]);
const preview = reactive({
  keyword: '',
  platformId: '',
  categoryId: '',
  brandId: '',
});

watch(
  [() => filters.keyword, () => filters.ruleType, () => filters.scopeType, () => filters.status],
  () => {
    page.value = 1;
  },
);

function ruleDisplay(row: any) {
  const terms = String(row.terms || '').split(',').filter(Boolean);
  if (row.ruleType === 1) return terms.join('  /  ');
  return terms.length > 1 ? `${terms[0]}  →  ${terms.slice(1).join(' / ')}` : row.terms;
}

function ruleTagType(type: number) {
  return ({ 1: 'success', 2: 'primary', 3: 'warning', 4: 'info' } as Record<number, string>)[type] || 'info';
}

function scopeLabel(row: any) {
  if (row.scopeType === 1) return '全局';
  const list = row.scopeType === 2 ? platforms.value : row.scopeType === 3 ? categories.value : brands.value;
  const hit = list.find((item) => String(item.id) === String(row.scopeId));
  return `${row.scopeTypeName} · ${hit?.name || row.scopeId}`;
}

async function load() {
  loading.value = true;
  try {
    rows.value = await request('/gateway/products/SearchIndex/Synonyms/Rules/List', {
      body: {},
      silent: true,
    }) || [];
  } catch {
    rows.value = [];
  } finally {
    loading.value = false;
  }
}

function flattenCategories(nodes: any[], output: any[] = []) {
  for (const node of nodes || []) {
    output.push({ id: String(node.id), name: node.categoryName });
    flattenCategories(node.children || [], output);
  }
  return output;
}

async function loadOptions() {
  optionsLoading.value = true;
  try {
    const [platformRows, categoryTree, brandRows] = await Promise.all([
      request('/gateway/platforms/Options', { method: 'GET', silent: true }),
      request('/gateway/categories/Tree', { method: 'GET', silent: true }),
      request('/gateway/brands/Options?limit=200', { method: 'GET', silent: true }),
    ]);
    platforms.value = (platformRows || []).map((item: any) => ({ id: String(item.id), name: item.name }));
    categories.value = flattenCategories(categoryTree || []);
    brands.value = (brandRows || []).map((item: any) => ({ id: String(item.id), name: item.name }));
    if (!preview.platformId && platforms.value.length) preview.platformId = platforms.value[0].id;
  } catch {
    platforms.value = [];
    categories.value = [];
    brands.value = [];
  } finally {
    optionsLoading.value = false;
  }
}

function resetForm() {
  editingId.value = '';
  form.terms = '';
  form.ruleType = 1;
  form.scopeType = 1;
  form.scopeId = '';
  form.priority = 100;
  form.status = 1;
  form.remark = '';
}

function openCreate() {
  resetForm();
  editorOpen.value = true;
}

function openEdit(row: any) {
  editingId.value = String(row.id);
  form.terms = row.terms;
  form.ruleType = row.ruleType;
  form.scopeType = row.scopeType;
  form.scopeId = String(row.scopeId || '');
  form.priority = row.priority;
  form.status = row.status;
  form.remark = row.remark || '';
  editorOpen.value = true;
}

function onRuleTypeChange() {
  // 类型变化不影响词条，保留内容便于运营调整。
}

function onScopeTypeChange() {
  form.scopeId = '';
}

async function save() {
  if (!form.terms.trim()) {
    ElMessage.warning('请填写同义词词条');
    return;
  }
  if (form.scopeType !== 1 && !form.scopeId) {
    ElMessage.warning('请选择作用域对象');
    return;
  }

  saving.value = true;
  try {
    await request('/gateway/products/SearchIndex/Synonyms/Rules/Save', {
      body: {
        id: editingId.value || '0',
        terms: form.terms,
        ruleType: form.ruleType,
        scopeType: form.scopeType,
        scopeId: form.scopeType === 1 ? '0' : form.scopeId,
        priority: form.priority,
        status: form.status,
        remark: form.remark,
      },
    });
    ElMessage.success('规则已保存，查询时即时生效');
    editorOpen.value = false;
    await load();
  } catch {
    // request 已提示
  } finally {
    saving.value = false;
  }
}

async function toggleStatus(row: any) {
  const next = row.status;
  try {
    await request('/gateway/products/SearchIndex/Synonyms/Rules/SetStatus', {
      body: { id: row.id, status: next },
      silent: true,
    });
    ElMessage.success(next === 1 ? '规则已启用' : '规则已停用');
  } catch {
    row.status = next === 1 ? 0 : 1;
  }
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(
      `确认删除「${ruleDisplay(row)}」？删除后立即停止参与搜索。`,
      '删除同义词规则',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    );
    await request('/gateway/products/SearchIndex/Synonyms/Rules/Delete', {
      body: { id: row.id },
    });
    ElMessage.success('规则已删除');
    await load();
  } catch {
    // 取消删除或 request 已提示
  }
}

function openPreview() {
  previewOpen.value = true;
  previewRows.value = [];
}

async function runPreview() {
  if (!preview.keyword.trim()) {
    ElMessage.warning('请输入要预览的关键词');
    return;
  }
  previewLoading.value = true;
  try {
    previewRows.value = await request('/gateway/products/SearchIndex/Synonyms/Preview', {
      body: {
        keyword: preview.keyword,
        platformId: preview.platformId || '0',
        categoryId: preview.categoryId || '0',
        brandId: preview.brandId || '0',
      },
    }) || [];
  } catch {
    previewRows.value = [];
  } finally {
    previewLoading.value = false;
  }
}

onMounted(async () => {
  await Promise.all([load(), loadOptions()]);
});
</script>

<style scoped>
.head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--space-4);
  margin-bottom: var(--space-5);
}

.head__title {
  margin: 0;
  font-size: var(--text-display);
  line-height: var(--lh-display);
  font-weight: 600;
}

.head__desc {
  margin: var(--space-1) 0 0;
  font-size: var(--text-sub);
  color: var(--text-2);
}

.head__actions {
  display: flex;
  gap: var(--space-2);
  flex: none;
}

.panel {
  border-radius: var(--radius-card);
  background: var(--bg-card);
  box-shadow: var(--shadow-card);
}

.panel--filters {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-bottom: var(--space-3);
  padding: var(--space-3) var(--space-4);
}

.panel--table {
  overflow: hidden;
}

.pager {
  display: flex;
  justify-content: flex-end;
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--hairline);
}

.filter__keyword {
  width: 280px;
}

.filter__select {
  width: 132px;
}

.filter__count {
  margin-left: auto;
  font-size: var(--text-foot);
  color: var(--text-2);
  font-variant-numeric: tabular-nums;
}

.rule-terms {
  font-weight: 500;
  color: var(--text-1);
  overflow-wrap: anywhere;
}

.scope-name {
  color: var(--text-1);
}

.scope-id {
  margin-left: var(--space-1);
  font-family: var(--font-mono);
  font-size: var(--text-note);
  color: var(--text-3);
}

.editor__row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-3);
}

.editor__half {
  min-width: 0;
}

.editor__help {
  margin: var(--space-1) 0 0;
  font-size: var(--text-foot);
  line-height: var(--lh-foot);
  color: var(--text-2);
}

.preview__form {
  display: flex;
  gap: var(--space-2);
}

.preview__keyword {
  flex: 1;
}

.preview__scope {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--space-2);
  margin: var(--space-3) 0;
}

@media (max-width: 900px) {
  .head {
    align-items: flex-start;
    flex-direction: column;
  }

  .panel--filters {
    flex-wrap: wrap;
  }

  .filter__keyword {
    width: 100%;
  }

  .filter__count {
    width: 100%;
    margin-left: 0;
  }
}
</style>
