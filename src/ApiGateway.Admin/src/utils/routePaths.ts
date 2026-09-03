const terminalCatchAllPattern = /\/\{\*\*[^/{}]+\}$/;

export function parseRoutePath(value: string) {
  const path = value.trim() || '/';
  const match = terminalCatchAllPattern.exec(path);

  if (!match)
    return { path, matchSubpaths: false };

  return {
    path: path.slice(0, match.index) || '/',
    matchSubpaths: true,
  };
}

export function buildRoutePath(value: string, matchSubpaths: boolean) {
  const path = value.trim() || '/';
  const basePath = path.replace(terminalCatchAllPattern, '') || '/';

  if (!matchSubpaths)
    return basePath;

  return basePath === '/'
    ? '/{**remainder}'
    : `${basePath.replace(/\/+$/, '')}/{**remainder}`;
}
