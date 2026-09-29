package main

import (
	"log"
	"net/http"
	"os"

	"marketplace/orders/internal/api"
)

func main() {
	addr := os.Getenv("ORDERS_ADDR")
	if addr == "" {
		addr = ":5004"
	}

	log.Printf("orders service listening on %s", addr)
	if err := http.ListenAndServe(addr, api.NewRouter()); err != nil {
		log.Fatal(err)
	}
}
