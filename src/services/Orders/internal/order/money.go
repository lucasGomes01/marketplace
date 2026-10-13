package order

import (
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

var decimalPattern = regexp.MustCompile(`^\d+(\.\d{1,2})?$`)

// ParseCents converte um decimal como chega nos eventos JSON ("199.90", "89.5", "249") para centavos,
// sem passar por float. Mais de 2 casas, sinal, expoente ou vírgula são recusados.
func ParseCents(decimal string) (int64, error) {
	if !decimalPattern.MatchString(decimal) {
		return 0, fmt.Errorf("invalid amount %q: expected digits with up to 2 decimal places", decimal)
	}
	units, fraction, _ := strings.Cut(decimal, ".")
	fraction = (fraction + "00")[:2]
	cents, err := strconv.ParseInt(units+fraction, 10, 64)
	if err != nil {
		return 0, fmt.Errorf("invalid amount %q: %w", decimal, err)
	}
	return cents, nil
}

// FormatCents devolve o decimal com 2 casas ("199.90"), para JSON e logs.
func FormatCents(cents int64) string {
	return fmt.Sprintf("%d.%02d", cents/100, cents%100)
}
