import { expect, test, type Page } from '@playwright/test'

// Contas novas a cada execução: o teste roda contra um banco que pode já ter dados.
const run = `${Date.now()}${Math.floor(Math.random() * 1000)}`
const password = 'Senha-forte-123'

async function register(page: Page, name: string, email: string) {
  await page.goto('/cadastro')
  await page.getByLabel('Nome').fill(name)
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Senha').fill(password)
  await page.getByRole('button', { name: 'Criar conta' }).click()
  await expect(page).toHaveURL(/\/minha-conta$/)
}

async function logout(page: Page) {
  await page.getByRole('button', { name: 'Sair' }).click()
  await expect(page.getByRole('link', { name: 'Entrar' })).toBeVisible()
}

async function login(page: Page, email: string) {
  await page.goto('/entrar')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Senha').fill(password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('button', { name: 'Sair' })).toBeVisible()
}

test('caminho feliz: vender, comprar, despachar e receber', async ({ page }) => {
  const sellerEmail = `vendedora-${run}@e2e.test`
  const buyerEmail = `comprador-${run}@e2e.test`
  const productName = `Furadeira E2E ${run}`

  // Vendedora: cria a conta, ativa o perfil de vendedor e anuncia um produto com 3 unidades.
  await register(page, 'Vendedora E2E', sellerEmail)
  await page.getByRole('button', { name: 'Começar a vender' }).click()
  await page.getByRole('link', { name: 'Vender' }).click()
  await page.getByLabel('Nome do produto').fill(productName)
  await page.getByLabel('Preço (R$)').fill('199,90')
  await page.getByLabel('Estoque').fill('3')
  await page.getByRole('button', { name: 'Publicar produto' }).click()
  await expect(page.getByText('Produto publicado!')).toBeVisible()
  const productUrl = page.url()
  await logout(page)

  // Comprador: cria a conta, coloca 2 unidades no carrinho e finaliza.
  await register(page, 'Comprador E2E', buyerEmail)
  await page.goto(productUrl)
  await expect(page.getByRole('heading', { name: productName })).toBeVisible()
  await page.getByRole('button', { name: 'Adicionar ao carrinho' }).click()
  await page.getByRole('link', { name: 'Carrinho (1)' }).click()
  await page.getByRole('button', { name: `Aumentar quantidade de ${productName}` }).click()
  await expect(page.getByTestId('cart-total')).toHaveText(/R\$\s399,80/)
  await page.getByRole('button', { name: 'Finalizar pedido' }).click()

  // O pedido nasce e é confirmado de forma assíncrona (Cart → Kafka → Orders → Catalog → Orders).
  await expect(page.getByRole('heading', { name: 'Pedido recebido' })).toBeVisible()
  await expect(page.getByText('Confirmado', { exact: true })).toBeVisible({ timeout: 60_000 })
  const orderUrl = page.url()
  await expect(page.getByTestId('order-total')).toHaveText(/R\$\s399,80/)
  await expect(page.getByRole('link', { name: 'Carrinho (0)' })).toBeVisible()

  // Aparece em "Meus pedidos" e o estoque baixou de 3 para 1.
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click()
  const row = page.getByRole('listitem').filter({ hasText: productName })
  await expect(row).toContainText('Confirmado')
  await expect(row).toContainText('2 itens')

  // O gateway guarda a página do produto por alguns segundos (cache): recarrega até o estoque novo aparecer.
  await expect(async () => {
    await page.goto(productUrl)
    await expect(page.getByText('Última unidade')).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 20_000 })
  await logout(page)

  // Vendedora despacha: separado → embalado → enviado.
  await login(page, sellerEmail)
  await page.getByRole('link', { name: 'Despachar' }).click()
  const toShip = page.getByRole('region', { name: 'A despachar' }).getByRole('article').filter({ hasText: productName })
  await expect(toShip).toContainText('Aguardando separação')
  for (const step of ['separado', 'embalado']) {
    await toShip.getByRole('button', { name: `Marcar como ${step}` }).click()
    await expect(toShip).toContainText(step.charAt(0).toUpperCase() + step.slice(1))
  }
  await toShip.getByRole('button', { name: 'Marcar como enviado' }).click()
  const shipped = page.getByRole('region', { name: 'Enviados' }).getByRole('article').filter({ hasText: productName })
  await expect(shipped).toContainText('Aguardando o comprador confirmar o recebimento.')
  await logout(page)

  // Comprador vê o envio a caminho e confirma o recebimento.
  await login(page, buyerEmail)
  await page.goto(orderUrl)
  const shipment = page.getByRole('region', { name: 'Entrega' }).getByRole('article', { name: 'Envio 1 de 1' })
  await expect(shipment.locator('.badge')).toHaveText('Enviado')
  await shipment.getByRole('button', { name: 'Recebi' }).click()
  await expect(shipment.locator('.badge')).toHaveText('Entregue')

  // Em "Meus pedidos" a entrega aparece concluída.
  await page.getByRole('link', { name: 'Pedidos', exact: true }).click()
  await expect(page.getByRole('listitem').filter({ hasText: productName })).toContainText('Entrega: Entregue')
})
