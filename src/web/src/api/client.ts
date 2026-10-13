export type FieldErrors = Record<string, string>

const UNEXPECTED_ERROR = 'Erro inesperado. Tente novamente.'

/** Erro de uma chamada à API, já com os erros por campo do ValidationProblemDetails do .NET. */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly fieldErrors: FieldErrors

  constructor(status: number, title: string, fieldErrors: FieldErrors = {}) {
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.fieldErrors = fieldErrors
  }
}

interface ProblemDetails {
  title?: string
  errors?: Record<string, string[]>
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  token?: string | null
  signal?: AbortSignal
}

export async function apiFetch<T = void>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, token, signal } = options
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }

  const response = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })

  if (!response.ok) {
    throw await toApiError(response)
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    const text = await response.text()
    problem = text ? (JSON.parse(text) as ProblemDetails) : {}
  } catch {
    // Corpo que não é JSON: fica só o status.
  }

  const fieldErrors: FieldErrors = {}
  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    if (messages.length > 0) {
      fieldErrors[field] = messages[0]
    }
  }

  return new ApiError(response.status, problem.title ?? UNEXPECTED_ERROR, fieldErrors)
}
