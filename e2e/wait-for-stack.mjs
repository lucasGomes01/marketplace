// Espera o backend do compose responder pelo gateway antes dos testes E2E (até 5 min).
const gateway = process.env.GATEWAY_URL ?? 'http://localhost:5000'
const services = ['users', 'catalog', 'cart', 'orders']
const deadline = Date.now() + 5 * 60_000

for (const service of services) {
  for (;;) {
    try {
      const response = await fetch(`${gateway}/health/${service}`)
      if (response.ok) {
        console.log(`${service}: ok`)
        break
      }
    } catch {
      // Ainda subindo.
    }
    if (Date.now() > deadline) {
      console.error(`${service} não respondeu em ${gateway}/health/${service}`)
      process.exit(1)
    }
    await new Promise((resolve) => setTimeout(resolve, 2000))
  }
}
