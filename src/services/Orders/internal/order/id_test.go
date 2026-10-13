package order

import (
	"regexp"
	"testing"
	"time"
)

var uuidV7 = regexp.MustCompile(`^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`)

func TestNewIDIsAVersion7UUID(t *testing.T) {
	id := NewID(now)

	if !uuidV7.MatchString(id) {
		t.Fatalf("%q is not a UUID v7", id)
	}
}

func TestNewIDsAreUniqueAndOrderedByTime(t *testing.T) {
	earlier := NewID(now)
	later := NewID(now.Add(time.Millisecond))
	again := NewID(now)

	if earlier >= later {
		t.Errorf("expected %q < %q", earlier, later)
	}
	if earlier == again {
		t.Errorf("two IDs for the same instant are equal: %q", earlier)
	}
}
