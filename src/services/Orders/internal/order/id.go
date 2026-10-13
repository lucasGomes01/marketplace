package order

import (
	"crypto/rand"
	"encoding/hex"
	"time"
)

// NewID gera um UUID v7 (RFC 9562): 48 bits de timestamp em ms + aleatório. IDs ficam ordenáveis por tempo,
// como os Guid.CreateVersion7() dos serviços .NET.
func NewID(now time.Time) string {
	var b [16]byte
	_, _ = rand.Read(b[6:]) // crypto/rand.Read nunca falha (Go 1.24+).

	ms := uint64(now.UnixMilli())
	b[0] = byte(ms >> 40)
	b[1] = byte(ms >> 32)
	b[2] = byte(ms >> 24)
	b[3] = byte(ms >> 16)
	b[4] = byte(ms >> 8)
	b[5] = byte(ms)
	b[6] = b[6]&0x0f | 0x70 // versão 7
	b[8] = b[8]&0x3f | 0x80 // variante RFC 4122

	h := hex.EncodeToString(b[:])
	return h[0:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:32]
}
