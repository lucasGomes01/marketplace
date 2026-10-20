// Gera observability/dashboards/marketplace.json (o painel provisionado no Grafana).
// Edite aqui e rode: node observability/build-dashboard.mjs
import { writeFileSync } from 'node:fs'

const prom = { type: 'prometheus', uid: 'prometheus' }
const tempo = { type: 'tempo', uid: 'tempo' }
const svc = 'service_namespace="marketplace"'
const k6 = 'versao=~"$versao", cenario=~"$cenario"'

let id = 0
let y = 0
const panels = []

function row(title) {
  panels.push({ type: 'row', id: ++id, title, collapsed: false, gridPos: { h: 1, w: 24, x: 0, y }, panels: [] })
  y += 1
}

function timeseries(title, description, w, x, targets, { unit = 'short', h = 8, min = 0 } = {}) {
  panels.push({
    type: 'timeseries', id: ++id, title, description, datasource: prom,
    gridPos: { h, w, x, y },
    fieldConfig: { defaults: { unit, min, custom: { lineWidth: 2, fillOpacity: 8, showPoints: 'never' } }, overrides: [] },
    options: { legend: { displayMode: 'table', placement: 'bottom', calcs: ['max', 'mean'] }, tooltip: { mode: 'multi', sort: 'desc' } },
    targets: targets.map(([expr, legendFormat], i) => ({ refId: String.fromCharCode(65 + i), datasource: prom, expr, legendFormat })),
  })
}

function bars(title, description, w, x, expr, legendFormat, unit) {
  panels.push({
    type: 'bargauge', id: ++id, title, description, datasource: prom,
    gridPos: { h: 7, w, x, y },
    fieldConfig: { defaults: { unit, min: 0, color: { mode: 'palette-classic' } }, overrides: [] },
    options: { orientation: 'horizontal', displayMode: 'basic', showUnfilled: true, valueMode: 'text', namePlacement: 'left' },
    targets: [{ refId: 'A', datasource: prom, expr, legendFormat, instant: true, range: false }],
  })
}

const rate = (metric, by, filter = svc) => `sum by (${by}) (rate(${metric}{${filter}}[$__rate_interval]))`
const p95 = (metric, by, filter = svc) =>
  `histogram_quantile(0.95, sum by (le, ${by}) (rate(${metric}_bucket{${filter}}[$__rate_interval])))`

// ---------------------------------------------------------------- Serviços
row('Serviços — taxa, erros e duração (RED)')
timeseries('Requisições por segundo', 'Requisições recebidas por serviço (gateway inclui todas as do site).', 8, 0,
  [[rate('http_server_request_duration_seconds_count', 'service_name'), '{{service_name}}']], { unit: 'reqps' })
timeseries('Duração p95', '95% das requisições terminaram abaixo deste tempo.', 8, 8,
  [[p95('http_server_request_duration_seconds', 'service_name'), '{{service_name}}']], { unit: 's' })
timeseries('Erros 5xx por segundo', 'Falhas do servidor. 4xx (validação, login) não entram.', 8, 16,
  [[rate('http_server_request_duration_seconds_count', 'service_name', `${svc}, http_response_status_code=~"5.."`), '{{service_name}}']],
  { unit: 'reqps' })
y += 8
timeseries('Requisições por cópia', 'Com --scale, mostra se a carga está dividida entre as cópias de cada serviço.', 12, 0,
  [[rate('http_server_request_duration_seconds_count', 'service_name, service_instance_id'), '{{service_name}} · {{service_instance_id}}']],
  { unit: 'reqps' })
timeseries('CPU por cópia (.NET)', '100% = um núcleo inteiro. O Orders (Go) não entra aqui.', 12, 12,
  [[`sum by (service_name, service_instance_id) (rate(dotnet_process_cpu_time_seconds_total{${svc}}[$__rate_interval])) * 100`,
    '{{service_name}} · {{service_instance_id}}']], { unit: 'percent' })
y += 8

// ---------------------------------------------------------------- Negócio
row('Pedidos e estoque')
timeseries('Pedidos por minuto', 'placed = criado pelo checkout; confirmed/rejected = resultado da reserva de estoque.', 8, 0,
  [[`sum by (status) (rate(marketplace_orders_total[$__rate_interval])) * 60`, '{{status}}']])
timeseries('Tempo até o pedido ser resolvido', 'Do pedido criado até Confirmado/Recusado: passa por Kafka → Catalog → Kafka. Sobe quando há fila nos consumidores.', 8, 8,
  [[`histogram_quantile(0.5, sum by (le) (rate(marketplace_order_settle_duration_seconds_bucket[$__rate_interval])))`, 'mediana'],
   [`histogram_quantile(0.95, sum by (le) (rate(marketplace_order_settle_duration_seconds_bucket[$__rate_interval])))`, 'p95']],
  { unit: 's' })
timeseries('Reservas de estoque por minuto', 'reserved = estoque baixado; rejected = faltou estoque.', 8, 16,
  [[`sum by (outcome) (rate(marketplace_stock_reservations_total[$__rate_interval])) * 60`, '{{outcome}}']])
y += 8

// ---------------------------------------------------------------- Outbox
row('Outbox — eventos esperando o Kafka (ADR-005)')
timeseries('Eventos pendentes no outbox', 'Gravados junto com o dado e ainda não publicados. Perto de 0 = relay em dia; subindo = Kafka fora ou relay atrasado. Só a cópia com o relay ativo informa.', 12, 0,
  [[`max by (service_name) (marketplace_outbox_pending{${svc}})`, '{{service_name}}']])
timeseries('Idade do evento pendente mais antigo', 'Quanto o evento mais velho da fila espera para ir ao Kafka. É o atraso extra que o outbox pode causar.', 12, 12,
  [[`max by (service_name) (marketplace_outbox_oldest_age_seconds{${svc}})`, '{{service_name}}']], { unit: 's' })
y += 8

// ---------------------------------------------------------------- k6
row('Testes de carga (k6) — compare versões')
bars('Pico de requisições/s por versão', 'Maior vazão sustentada (média de 1 min) de cada versão no período escolhido.', 12, 0,
  `max by (versao, cenario) (max_over_time((sum by (versao, cenario) (rate(k6_http_reqs_total{${k6}}[1m])))[$__range:15s]))`,
  '{{versao}} · {{cenario}}', 'reqps')
bars('Pior p95 por versão', 'Maior p95 de duração visto pelo k6 em cada versão. Menor é melhor.', 12, 12,
  `max by (versao, cenario) (max_over_time(k6_http_req_duration_p95{${k6}}[$__range]))`, '{{versao}} · {{cenario}}', 's')
y += 7
timeseries('Requisições/s (k6)', 'Vazão vista pelo k6 ao longo do teste.', 8, 0,
  [[`sum by (versao, cenario) (rate(k6_http_reqs_total{${k6}}[$__rate_interval]))`, '{{versao}} · {{cenario}}']], { unit: 'reqps' })
timeseries('Duração p95 (k6)', 'Do ponto de vista do usuário virtual (inclui a fila no gateway).', 8, 8,
  [[`max by (versao, cenario) (k6_http_req_duration_p95{${k6}})`, '{{versao}} · {{cenario}}']], { unit: 's' })
timeseries('Usuários virtuais e falhas', 'VUs ativos e taxa de requisições com falha.', 8, 16,
  [[`sum by (versao, cenario) (k6_vus{${k6}})`, 'VUs {{versao}}'],
   [`avg by (versao, cenario) (k6_http_req_failed_rate{${k6}}) * 100`, '% falhas {{versao}}']])
y += 8

// ---------------------------------------------------------------- Traces
row('Traces lentos')
panels.push({
  type: 'table', id: ++id, title: 'Requisições mais lentas que 500 ms',
  description: 'Clique no trace para ver o caminho completo (ex.: checkout → Kafka → Orders → Catalog).',
  datasource: tempo, gridPos: { h: 10, w: 24, x: 0, y },
  targets: [{ refId: 'A', datasource: tempo, queryType: 'traceql', limit: 20, tableType: 'traces',
    query: '{ resource.service.namespace = "marketplace" && duration > 500ms }' }],
})
y += 10

const variable = (name, label, metric) => ({
  name, label, type: 'query', datasource: prom, query: { query: `label_values(${metric}, ${name})`, refId: 'v' },
  definition: `label_values(${metric}, ${name})`, refresh: 2, multi: true, includeAll: true,
  current: { selected: true, text: ['All'], value: ['$__all'] }, allValue: '.*', sort: 1,
})

const dashboard = {
  uid: 'marketplace',
  title: 'Marketplace',
  description: 'Serviços, pedidos e testes de carga. Marcas laranja = início/fim de um teste k6.',
  tags: ['marketplace'],
  timezone: 'browser',
  schemaVersion: 39,
  refresh: '10s',
  time: { from: 'now-30m', to: 'now' },
  annotations: { list: [{
    name: 'Testes de carga', enable: true, iconColor: 'orange',
    datasource: { type: 'grafana', uid: '-- Grafana --' },
    target: { type: 'tags', tags: ['k6'], matchAny: true, limit: 200 },
  }] },
  templating: { list: [variable('versao', 'Versão', 'k6_http_reqs_total'), variable('cenario', 'Cenário', 'k6_http_reqs_total')] },
  panels,
}

writeFileSync(new URL('./dashboards/marketplace.json', import.meta.url), JSON.stringify(dashboard, null, 2) + '\n')
console.log(`${panels.length} painéis`)
