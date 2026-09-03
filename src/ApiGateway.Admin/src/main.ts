import { createApp } from 'vue';
import App from './App.vue';
import { i18n } from './i18n';
import router from './router';
import '@fontsource-variable/inter';
import '@aditify/identity/styles.css';
import './styles.css';

createApp(App).use(router).use(i18n).mount('#app');
