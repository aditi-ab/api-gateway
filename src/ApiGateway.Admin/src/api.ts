let antiforgeryToken = '';
let bearerToken = '';

export function setAntiforgeryToken(value: string) { antiforgeryToken = value; }
export function setBearerToken(value: string) { bearerToken = value; }
export async function graphql<T>(query: string, variables: Record<string, unknown> = {}): Promise<T> {
  const response = await fetch('/graphql', { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', ...(bearerToken ? { Authorization: `Bearer ${bearerToken}` } : antiforgeryToken ? { 'X-CSRF-TOKEN': antiforgeryToken } : {}) }, body: JSON.stringify({ query, variables }) });
  const body = await response.text();
  let payload: { data?: T; errors?: Array<{ message: string; extensions?: { issues?: Array<{ message?: string }> } }> } = {};

  if (body) {
    try { payload = JSON.parse(body) as typeof payload; }
    catch {
      throw new Error(response.ok ? 'The server returned an invalid response.' : `Request failed (${response.status})`);
    }
  }

  if (!response.ok || payload.errors?.length) {
    const messages = payload.errors?.flatMap((error) => {
      const issues = error.extensions?.issues?.map(issue => issue.message).filter(Boolean) as string[] | undefined;

      return issues?.length ? issues : [error.message];
    });

    throw new Error(messages?.join('\n') || `Request failed (${response.status})`);
  }

  return payload.data as T;
}
