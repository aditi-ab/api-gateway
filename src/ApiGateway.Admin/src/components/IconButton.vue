<template>
  <Tooltip>
    <TooltipTrigger as-child>
      <span class="inline-flex">
        <Button
          v-bind="$attrs"
          :aria-label="label"
          :class="props.class"
          :disabled="disabled"
          :size="size"
          :variant="variant"
        >
          <slot />
        </Button>
      </span>
    </TooltipTrigger>
    <TooltipContent :side-offset="4">
      {{ label }}
    </TooltipContent>
  </Tooltip>
</template>

<script setup lang="ts">
import type { ButtonVariants } from '@aditify/ui';
import type { HTMLAttributes } from 'vue';
import { Button, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';

defineOptions({ inheritAttrs: false });

const props = withDefaults(defineProps<{
  class?: HTMLAttributes['class'];
  disabled?: boolean;
  label: string;
  size?: ButtonVariants['size'];
  variant?: ButtonVariants['variant'];
}>(), {
  size: 'icon-sm',
  variant: 'ghost',
});
</script>
