export interface RouteTransformEntry { key: string; value: string }
export type RouteTransform = RouteTransformEntry[];

export function transformValue(transform: RouteTransform, key: string) {
  return transform.find(entry => entry.key.toLowerCase() === key.toLowerCase())?.value;
}

export function isHeaderTransform(transform: RouteTransform) {
  return transformValue(transform, 'RequestHeader') !== undefined
    || transformValue(transform, 'ResponseHeader') !== undefined;
}

export function isPathTransform(transform: RouteTransform) {
  return transformValue(transform, 'PathRemovePrefix') !== undefined;
}

export function preservesOriginalHost(transforms: RouteTransform[] | undefined) {
  return transforms?.some(transform => transformValue(transform, 'RequestHeaderOriginalHost')?.toLowerCase() === 'true') ?? false;
}

export function replaceFirstTransform(
  transforms: RouteTransform[] | undefined,
  predicate: (transform: RouteTransform) => boolean,
  replacement: RouteTransform,
) {
  const result = [...(transforms ?? [])];
  const index = result.findIndex(predicate);

  if (index >= 0)
    result[index] = replacement;
  else
    result.push(replacement);

  return result;
}

export function removeTransforms(
  transforms: RouteTransform[] | undefined,
  predicate: (transform: RouteTransform) => boolean,
) {
  return (transforms ?? []).filter(transform => !predicate(transform));
}
