// AcroForm list + fill sidecar for Netie Space. Apache-2.0 via pdfcpu.
// Usage: netie-pdf-form form-list|form-fill < stdin JSON
package main

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/pdfcpu/pdfcpu/pkg/api"
	"github.com/pdfcpu/pdfcpu/pkg/pdfcpu/model"
)

type inJob struct {
	Job              string      `json:"job"`
	In               string      `json:"in"`
	Out              string      `json:"out"`
	NeedAppearances  bool        `json:"needAppearances"`
	Fields           []fillField `json:"fields"`
}

type fillField struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type fieldOut struct {
	Name string `json:"name"`
	Type string `json:"type"`
	Page int    `json:"page"`
	X    float64 `json:"x"`
	Y    float64 `json:"y"`
	W    float64 `json:"w"`
	H    float64 `json:"h"`
}

type listOut struct {
	Ok     bool       `json:"ok"`
	Xfa    bool       `json:"xfa"`
	Fields []fieldOut `json:"fields,omitempty"`
	Error  string     `json:"error,omitempty"`
}

type fillOut struct {
	Ok      bool     `json:"ok"`
	Out     string   `json:"out,omitempty"`
	Filled  int      `json:"filled,omitempty"`
	Missing []string `json:"missing,omitempty"`
	Error   string   `json:"error,omitempty"`
}

func main() {
	cmd := ""
	if len(os.Args) > 1 {
		cmd = os.Args[1]
	}
	raw, _ := io.ReadAll(os.Stdin)
	var job inJob
	if err := json.Unmarshal(raw, &job); err != nil {
		fail(1, err.Error())
	}
	if job.Job != "" {
		cmd = job.Job
	}
	switch cmd {
	case "form-list":
		doList(job.In)
	case "form-fill":
		doFill(job)
	default:
		fmt.Fprintln(os.Stderr, "usage: netie-pdf-form form-list|form-fill")
		os.Exit(2)
	}
}

func doList(path string) {
	f, err := os.Open(path)
	if err != nil {
		emit(listOut{Ok: false, Error: err.Error()})
		os.Exit(1)
	}
	defer f.Close()

	conf := model.NewDefaultConfiguration()
	names, err := api.ListFormFields(f, conf)
	if err != nil {
		low := strings.ToLower(err.Error())
		if strings.Contains(low, "xfa") {
			emit(listOut{Ok: false, Xfa: true, Error: "xfa_unsupported"})
			os.Exit(0)
		}
		emit(listOut{Ok: false, Error: err.Error()})
		os.Exit(1)
	}
	if len(names) == 0 {
		emit(listOut{Ok: false, Error: "no_acroform"})
		os.Exit(0)
	}
	fields := make([]fieldOut, 0, len(names))
	for _, n := range names {
		fields = append(fields, fieldOut{Name: n, Type: "text", Page: 1})
	}
	emit(listOut{Ok: true, Fields: fields})
}

func doFill(job inJob) {
	if job.In == "" || job.Out == "" {
		emit(fillOut{Ok: false, Error: "in and out required"})
		os.Exit(1)
	}
	if len(job.Fields) == 0 {
		emit(fillOut{Ok: false, Error: "no fields"})
		os.Exit(1)
	}

	// pdfcpu FillFormFile wants a JSON form export. Build the minimal map.
	form := map[string]any{"forms": []any{map[string]any{}}}
	kv := map[string]string{}
	for _, f := range job.Fields {
		if strings.TrimSpace(f.Name) == "" {
			continue
		}
		kv[f.Name] = f.Value
	}
	form["forms"] = []any{map[string]any{"fields": kv}}
	tmp := filepath.Join(os.TempDir(), "netie_form_fill.json")
	b, _ := json.Marshal(form)
	if err := os.WriteFile(tmp, b, 0600); err != nil {
		emit(fillOut{Ok: false, Error: err.Error()})
		os.Exit(1)
	}
	defer os.Remove(tmp)

	conf := model.NewDefaultConfiguration()
	if err := api.FillFormFile(job.In, tmp, job.Out, conf); err != nil {
		emit(fillOut{Ok: false, Error: err.Error()})
		os.Exit(1)
	}
	emit(fillOut{Ok: true, Out: job.Out, Filled: len(kv)})
}

func emit(v any) {
	enc := json.NewEncoder(os.Stdout)
	_ = enc.Encode(v)
}

func fail(code int, msg string) {
	emit(fillOut{Ok: false, Error: msg})
	os.Exit(code)
}
