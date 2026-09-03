<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ $t('nav.system') }}
        </p>
        <h1>{{ t('title') }}</h1>
        <p class="page-lead">
          {{ t('lead') }}
        </p>
      </div>
      <Button variant="outline" @click="load">
        <RefreshCw />{{ t('refresh') }}
      </Button>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('time') }}</TableHead><TableHead>{{ t('actor') }}</TableHead><TableHead>{{ t('action') }}</TableHead><TableHead>{{ t('target') }}</TableHead><TableHead>{{ t('correlation') }}</TableHead></TableRow></TableHeader>
        <TableBody>
          <TableRow v-for="event in events" :key="event.id">
            <TableCell>{{ formatDateTime(event.occurredAtUtc) }}</TableCell><TableCell>{{ event.actorId }}</TableCell><TableCell>{{ event.action }}</TableCell><TableCell>{{ event.targetType }} <code>{{ event.targetId }}</code></TableCell><TableCell><code>{{ event.correlationId }}</code></TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Button, Card, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, RefreshCw } from '@lucide/vue';
import { onMounted, ref } from 'vue'; import { useI18n } from 'vue-i18n'; import { graphql } from '../api'; import { formatDateTime } from '../utils/dateTime';

interface Event { id: string; occurredAtUtc: string; actorId: string; action: string; targetType: string; targetId: string; correlationId: string }

const events = ref<Event[]>([]); const error = ref('');
const { t } = useI18n();

async function load() {
  try { events.value = (await graphql<{ auditEvents: Event[] }>(`query{auditEvents(take:200){id occurredAtUtc actorId action targetType targetId correlationId}}`)).auditEvents; }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}onMounted(load);
</script>

<i18n lang="json">
{
  "en": { "title": "Audit history", "lead": "Review configuration and administration changes.", "refresh": "Refresh", "time": "Time", "actor": "Actor", "action": "Action", "target": "Target", "correlation": "Correlation" },
  "sv": { "title": "Granskningshistorik", "lead": "Granska konfigurations- och administrationsändringar.", "refresh": "Uppdatera", "time": "Tid", "actor": "Aktör", "action": "Åtgärd", "target": "Mål", "correlation": "Korrelation" }
}
</i18n>
