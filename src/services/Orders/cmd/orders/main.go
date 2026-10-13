// Orders: consome cart.checked-out, grava o pedido no SQL Server, publica order.placed e expõe /orders.
package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/twmb/franz-go/pkg/kgo"

	"marketplace/orders/internal/api"
	"marketplace/orders/internal/app"
	"marketplace/orders/internal/auth"
	"marketplace/orders/internal/messaging"
	"marketplace/orders/internal/store"
)

// consumerGroup segue a convenção "um grupo por serviço" (.ai/context/events.md).
const consumerGroup = "orders-service"

type config struct {
	addr    string
	dsn     string
	brokers []string
	auth    auth.Config
}

func loadConfig() (config, error) {
	cfg := config{
		addr:    env("ORDERS_ADDR", ":5004"),
		dsn:     os.Getenv("ORDERS_DB_DSN"),
		brokers: strings.Split(env("ORDERS_KAFKA_BROKERS", "localhost:9092"), ","),
		auth: auth.Config{
			Issuer:     env("JWT_ISSUER", "marketplace-users"),
			Audience:   env("JWT_AUDIENCE", "marketplace"),
			SigningKey: []byte(os.Getenv("JWT_SIGNING_KEY")),
		},
	}
	switch {
	case cfg.dsn == "":
		return cfg, errors.New("ORDERS_DB_DSN is required")
	case len(cfg.auth.SigningKey) < 32:
		return cfg, errors.New("JWT_SIGNING_KEY is required (at least 32 characters, same as the .NET services)")
	}
	return cfg, nil
}

func env(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func main() {
	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	slog.SetDefault(logger)
	if err := run(logger); err != nil {
		logger.Error("orders stopped", "error", err)
		os.Exit(1)
	}
}

func run(logger *slog.Logger) error {
	cfg, err := loadConfig()
	if err != nil {
		return err
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	if err := store.WaitReady(ctx, cfg.dsn); err != nil {
		return err
	}
	orders, err := store.Open(ctx, cfg.dsn)
	if err != nil {
		return err
	}
	defer orders.Close()

	producer, err := kgo.NewClient(kgo.SeedBrokers(cfg.brokers...))
	if err != nil {
		return fmt.Errorf("kafka producer: %w", err)
	}
	publisher := messaging.NewPublisher(producer, time.Now)
	defer publisher.Close()

	service := app.NewService(orders, publisher, time.Now)

	consumer, err := messaging.NewConsumer(cfg.brokers, consumerGroup, []string{app.EventCartCheckedOut}, checkoutHandler(service), logger)
	if err != nil {
		return fmt.Errorf("kafka consumer: %w", err)
	}
	defer consumer.Close()
	consumerDone := make(chan error, 1)
	go func() { consumerDone <- consumer.Run(ctx) }()

	server := &http.Server{Addr: cfg.addr, Handler: api.NewRouter(service, cfg.auth, time.Now), ReadHeaderTimeout: 5 * time.Second}
	serverDone := make(chan error, 1)
	go func() { serverDone <- server.ListenAndServe() }()
	logger.Info("orders service started", "addr", cfg.addr, "brokers", cfg.brokers, "group", consumerGroup)

	select {
	case <-ctx.Done():
	case err := <-serverDone:
		return err
	}
	shutdown, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	_ = server.Shutdown(shutdown)
	<-consumerDone
	return nil
}

// checkoutHandler traduz o envelope Kafka no caso de uso; dado inválido vira erro permanente (registra e pula).
func checkoutHandler(service *app.Service) messaging.Handler {
	return func(ctx context.Context, env messaging.Envelope) error {
		var event app.CartCheckedOut
		if err := json.Unmarshal(env.Data, &event); err != nil {
			return messaging.Permanent(fmt.Errorf("decode %s: %w", env.Type, err))
		}
		order, err := service.PlaceOrder(ctx, event)
		if errors.Is(err, app.ErrInvalidEvent) {
			return messaging.Permanent(err)
		}
		if err == nil {
			slog.InfoContext(ctx, "order placed", "orderId", order.ID, "buyerId", order.BuyerID, "total", order.Total().Cents)
		}
		return err
	}
}
