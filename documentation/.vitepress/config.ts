import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'API Gateway',
  description: 'Operate and integrate with API Gateway.',
  base: process.env.DOCS_BASE ?? '/docs/',
  outDir: process.env.DOCS_OUT_DIR ?? '../src/ApiGateway.Management/wwwroot/docs',
  themeConfig: {
    nav: [{ text: 'Guide', link: '/guide/getting-started' }, { text: 'Management API', link: '/management-api/' }],
    sidebar: [{ text: 'Guide', items: [{ text: 'Getting started', link: '/guide/getting-started' }, { text: 'Administration UI', link: '/guide/administration' }, { text: 'Architecture', link: '/guide/architecture' }, { text: 'Configuration lifecycle', link: '/guide/configuration' }, { text: 'Security', link: '/guide/security' }] }, { text: 'Reference', items: [{ text: 'Management API', link: '/management-api/' }, { text: 'Operations', link: '/operations/' }] }],
  },
})
