<template>
  <div class="page-container">
    <div class="flex items-end justify-between gap-4">
      <div>
        <h1 class="capitalize">
          {{ resource }}
        </h1><p class="mt-1 text-muted-foreground">
          Inspect the active configuration. Use the Draft Editor to make versioned changes.
        </p>
      </div>
      <Field class="w-70">
        <FieldLabel for="configuration-environment">
          Environment
        </FieldLabel>
        <Select v-model="environmentId">
          <SelectTrigger id="configuration-environment">
            <SelectValue placeholder="Environment" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="environment in environments" :key="environment.id" :value="environment.id">
              {{ environment.displayName }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-4">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-if="revision" class="mt-4">
      <Info /><AlertDescription>Showing active revision {{ revision.number }}.</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <Table v-if="rows.length">
        <TableHeader><TableRow><TableHead>ID</TableHead><TableHead>Configuration</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="(row, index) in rows" :key="row.id || index">
            <TableCell>
              <code>{{ row.id }}</code><div v-if="row.kind" class="text-xs">
                {{ row.kind }}
              </div>
            </TableCell><TableCell><pre>{{ JSON.stringify(row.value || row, null, 2) }}</pre></TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <Empty v-else>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <FileQuestion />
          </EmptyMedia><EmptyTitle>No active configuration</EmptyTitle><EmptyDescription>Publish a revision to populate this view.</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Card, Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle, Field, FieldLabel, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, FileQuestion, Info } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { graphql } from '../api';

const props = defineProps<{ resource: string }>();

interface Environment { id: string; displayName: string } interface Revision { number: number; configJson: string }

const environments = ref<Environment[]>([]); const environmentId = ref(''); const revision = ref<Revision | null>(null); const error = ref('');
const config = computed(() => revision.value ? JSON.parse(revision.value.configJson) : null); const rows = computed(() => props.resource === 'routes' ? config.value?.routes || [] : props.resource === 'clusters' ? config.value?.clusters || [] : props.resource === 'traffic' ? (config.value?.clusters || []).filter((x: any) => x.traffic) : Object.entries(config.value?.policies || {}).flatMap(([kind, values]) => Object.entries(values as object).map(([id, value]) => ({ id, kind, value }))));

async function initial() { environments.value = (await graphql<{ environments: Environment[] }>(`query{environments{id displayName}}`)).environments; environmentId.value = environments.value[0]?.id || ''; }
async function load() {
  if (!environmentId.value)
    return;

  try { revision.value = (await graphql<{ activeRevision: Revision | null }>(`query($id:UUID!){activeRevision(environmentId:$id){number configJson}}`, { id: environmentId.value })).activeRevision; }
  catch (e) { error.value = e instanceof Error ? e.message : String(e); }
}watch(environmentId, load); onMounted(initial);
</script>

<style scoped>
pre {
  white-space: pre-wrap;
  max-width: 80ch;
  font-size: 0.78rem;
}
</style>
