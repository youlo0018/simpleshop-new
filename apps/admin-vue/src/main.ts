import { createApp } from 'vue';
import { createPinia } from 'pinia';
import ElementPlus from 'element-plus';
import * as ElIcons from '@element-plus/icons-vue';
import zhCn from 'element-plus/es/locale/lang/zh-cn';
import 'element-plus/dist/index.css';

// 顺序不能换：token 必须先加载，否则 Element Plus 组件拿到的是无 token 的 CSS 变量，
// 首个页面会闪一下没有苹果风的样子。
import './styles.css';
import './element-theme.css';

import App from './App.vue';
import router from './router';

// 🔴 数字输入框禁掉「滚轮改值」。
//
// <input type="number"> 在**聚焦**状态下，鼠标滚轮悬停在它上面会直接改数值 ——
// 运营填完金额去滚动页面，25.50 会悄悄变成 24.50，而用户完全没察觉：
// 没有报错、没有提示，只有一张金额不对的订单。金额、库存、秒杀价都在这一类字段里。
//
// 做法是捕获阶段拦 wheel：把正在聚焦的数字框 blur 掉（Chrome / Edge 只在聚焦时
// 响应滚轮改值），然后**放行事件本身** —— 不 preventDefault，页面照常滚动，
// 否则用户会觉得「滚轮在输入框上卡住了」。
// 用全局监听而不是给每个 el-input 挂 @wheel：全站十几处数字框，
// 逐个挂必然漏（漏掉的那一处就是下一次金额事故）。
document.addEventListener(
  'wheel',
  (event) => {
    const target = event.target as HTMLElement | null;
    if (!target?.closest) return;

    // el-input-number 内部同样是 <input>，两个选择器都兜住
    const input = (target.closest('input[type="number"]')
      ?? target.closest('.el-input-number')?.querySelector('input')) as HTMLInputElement | null;

    if (input && document.activeElement === input) input.blur();
  },
  { capture: true },
);

const app = createApp(App);

// 侧边栏图标是按**字符串名**动态渲染的（<component :is="meta.icon" />），
// 而动态组件只能解析全局注册过的组件。
// 少注册这一步的症状是：菜单文字都在，图标位置一片空白——
// 页面不报错、构建也通过，只有肉眼看截图才发现。
Object.entries(ElIcons).forEach(([name, component]) => {
  app.component(name, component);
});

app.use(createPinia());
app.use(router);
app.use(ElementPlus, { locale: zhCn });

app.mount('#app');
