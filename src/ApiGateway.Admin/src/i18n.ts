import { createI18n } from 'vue-i18n';

export const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('apigateway-locale') || (navigator.language.toLowerCase().startsWith('sv') ? 'sv' : 'en'),
  fallbackLocale: 'en',
  messages: {
    en: {
      common: { saveUnpublished: 'Save as unpublished', createUnpublished: 'Create as unpublished', importUnpublished: 'Add to unpublished changes', savedUnpublished: 'Saved to unpublished changes.', staged: 'Staged' },
      nav: { dashboard: 'Overview', routes: 'Routes', activity: 'Activity', access: 'Access', users: 'Users', certificates: 'Certificates', system: 'System', environments: 'Environments', instances: 'Gateway instances', consumerKeys: 'Consumer keys', managementKeys: 'Management keys', audit: 'Audit', settings: 'Settings' },
    },
    sv: {
      common: { saveUnpublished: 'Spara som opublicerad', createUnpublished: 'Skapa som opublicerad', importUnpublished: 'Lägg till i opublicerade ändringar', savedUnpublished: 'Sparades i opublicerade ändringar.', staged: 'Stegvis' },
      nav: { dashboard: 'Översikt', routes: 'Routes', activity: 'Aktivitet', access: 'Åtkomst', users: 'Användare', certificates: 'Certifikat', system: 'System', environments: 'Miljöer', instances: 'Gatewayinstanser', consumerKeys: 'Konsumentnycklar', managementKeys: 'Hanteringsnycklar', audit: 'Granskning', settings: 'Inställningar' },
    },
  },
});
