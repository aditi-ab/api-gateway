import { createRouter, createWebHistory } from 'vue-router';

export default createRouter({
  history: createWebHistory('/admin/'),
  routes: [
    { path: '/', component: () => import('./views/DashboardView.vue'), meta: { title: 'Overview' } },
    { path: '/routes', component: () => import('./views/RoutesView.vue'), meta: { title: 'Routes' } },
    { path: '/upstreams', component: () => import('./views/UpstreamsView.vue'), meta: { title: 'Upstreams' } },
    { path: '/routes/:routeId', component: () => import('./views/RouteDetailView.vue'), meta: { title: 'Route' } },
    { path: '/activity', component: () => import('./views/ActivityView.vue'), meta: { title: 'Activity' } },
    { path: '/environments', component: () => import('./views/EnvironmentsView.vue'), meta: { title: 'Environments' } },
    { path: '/instances', component: () => import('./views/InstancesView.vue'), meta: { title: 'Gateway instances' } },
    { path: '/audit', component: () => import('./views/AuditView.vue'), meta: { title: 'Audit' } },
    { path: '/consumer-keys', component: () => import('./views/ConsumerKeysView.vue'), meta: { title: 'Consumer keys' } },
    { path: '/management-keys', component: () => import('./views/ManagementKeysView.vue'), meta: { title: 'Management keys' } },
    { path: '/users', component: () => import('./views/UsersView.vue'), meta: { title: 'Users and identity' } },
    { path: '/settings', component: () => import('./views/SettingsView.vue'), meta: { title: 'Settings' } },
    { path: '/certificates', component: () => import('./views/CertificatesView.vue'), meta: { title: 'Certificates' } },
  ],
});
