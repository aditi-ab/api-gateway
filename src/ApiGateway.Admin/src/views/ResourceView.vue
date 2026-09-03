<template>
  <div class="page-container">
    <h1 class="capitalize">
      {{ resource.replace(/-/g, ' ') }}
    </h1><p class="mt-1 text-muted-foreground">
      Manage {{ resource.replace(/-/g, ' ') }} for the selected environment.
    </p><Card class="mt-6">
      <CardContent>The guided workflow for this area is connected as its GraphQL contract becomes available.</CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Card, CardContent } from '@aditify/ui';

defineProps<{ resource: string }>();
</script>
