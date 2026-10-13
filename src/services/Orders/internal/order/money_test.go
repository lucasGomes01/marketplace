package order

import "testing"

func TestParseDecimalToCents(t *testing.T) {
	tests := []struct {
		in   string
		want int64
	}{
		{"199.90", 19990},
		{"199.9", 19990},
		{"89.5", 8950},
		{"249", 24900},
		{"0.01", 1},
		{"1234567.89", 123456789},
	}
	for _, tt := range tests {
		got, err := ParseCents(tt.in)
		if err != nil || got != tt.want {
			t.Errorf("ParseCents(%q) = %d, %v; want %d", tt.in, got, err, tt.want)
		}
	}
}

func TestParseDecimalRejectsInvalidValues(t *testing.T) {
	for _, in := range []string{"", "abc", "1.234", "-5", "1e3", "1.", ".5", "1,50"} {
		if _, err := ParseCents(in); err == nil {
			t.Errorf("ParseCents(%q) should fail", in)
		}
	}
}

func TestFormatCents(t *testing.T) {
	tests := map[int64]string{19990: "199.90", 8950: "89.50", 1: "0.01", 0: "0.00", 123456789: "1234567.89"}
	for in, want := range tests {
		if got := FormatCents(in); got != want {
			t.Errorf("FormatCents(%d) = %q, want %q", in, got, want)
		}
	}
}
