export class ApiError extends Error {
  constructor(public code: string, public fields: Record<string, string[]> = {}) { super(code); }
}

// The deadline covers headers AND body, and settles even if a transport ignores abort.
export async function request<T>(path: string, body: unknown, signal: AbortSignal, timeout = 60000): Promise<T> {
  const controller = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let cancel = () => {};
  const interrupted = new Promise<never>((_, reject) => {
    cancel = () => { reject(new ApiError('cancelled')); controller.abort(); };
    signal.addEventListener('abort', cancel, { once: true });
    if (signal.aborted) cancel();
    timer = setTimeout(() => { reject(new ApiError('request_timeout')); controller.abort(); }, timeout);
  });
  const operation = async () => {
    if (signal.aborted) throw new ApiError('cancelled');
    const response = await fetch(path, {
      method: body === undefined ? 'GET' : 'POST',
      ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
      signal: controller.signal,
    });
    if (path === '/health') {
      if (!response.ok) throw new ApiError('server_error');
      return undefined as T;
    }
    const data: unknown = await response.json().catch(() => null);
    if (!response.ok) {
      const problem = data && typeof data === 'object' ? data as Record<string, unknown> : {};
      const fields: Record<string, string[]> = {};
      if (problem.errors && typeof problem.errors === 'object') {
        for (const [key, codes] of Object.entries(problem.errors)) {
          if (Array.isArray(codes) && codes.every(code => typeof code === 'string')) fields[key] = codes;
        }
      }
      throw new ApiError(typeof problem.code === 'string' ? problem.code : Object.keys(fields).length ? 'validation_failed' : 'server_error', fields);
    }
    if (!data || typeof data !== 'object') throw new ApiError('invalid_response');
    return data as T;
  };
  try { return await Promise.race([operation(), interrupted]); }
  catch (error) { throw error instanceof ApiError ? error : new ApiError('network_error'); }
  finally { clearTimeout(timer); signal.removeEventListener('abort', cancel); }
}
