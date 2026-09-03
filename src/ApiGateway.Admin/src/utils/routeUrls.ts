export type InboundScheme = 'ANY' | 'HTTP_ONLY' | 'HTTPS_REDIRECT';

export interface RouteUrlSource {
  inbound: { scheme: InboundScheme };
  match: { hosts: string[]; path: string; methods: string[] };
}

export function routeTestUrls(route: RouteUrlSource) {
  const schemes = route.inbound.scheme === 'HTTP_ONLY'
    ? ['http']
    : route.inbound.scheme === 'HTTPS_REDIRECT' ? ['https'] : ['http', 'https'];
  const testPath = route.match.path.replace(/\{\*\*[^}]+\}/g, '');

  return route.match.hosts
    .filter(host => !host.includes('*'))
    .flatMap(host => schemes.map(scheme => `${scheme}://${host}${testPath}`));
}
