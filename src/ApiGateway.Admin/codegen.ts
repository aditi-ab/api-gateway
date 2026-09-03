import type { CodegenConfig } from '@graphql-codegen/cli';

const config: CodegenConfig = { schema: '../../schema.graphql', generates: { 'src/generated/graphql.ts': { plugins: ['typescript'], config: { enumsAsTypes: true, immutableTypes: true, maybeValue: 'T | null', scalars: { UUID: 'string', DateTime: 'string', Duration: 'string', Long: 'number' } } } } };

export default config;
