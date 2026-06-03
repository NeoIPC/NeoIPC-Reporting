#!/usr/bin/env Rscript
#
# Extracts the metadata block from a serializeJSON-wrapped neoipcr
# reference dataset and writes it to a separate file as plain JSON.
#
# Invoked by the NeoIPC.Reporting service when an admin uploads a
# reference dataset, so the service can index it by the filter set
# that shaped it without reimplementing R's unserializeJSON in C#.
#
# Usage:
#   Rscript --vanilla extract-reference-data-metadata.R \
#     --in <serialized-dataset.json> \
#     --out <metadata.json>

suppressPackageStartupMessages({
  library(jsonlite)
  library(neoipcr)
})

args <- commandArgs(trailingOnly = TRUE)
get_arg <- function(flag) {
  i <- match(flag, args)
  if (is.na(i) || i == length(args)) {
    stop("Missing value for ", flag, call. = FALSE)
  }
  args[[i + 1L]]
}

in_path <- get_arg("--in")
out_path <- get_arg("--out")

if (!file.exists(in_path)) {
  stop("Input file not found: ", in_path, call. = FALSE)
}

dataset <- tryCatch(
  jsonlite::unserializeJSON(readLines(in_path, warn = FALSE)),
  error = function(e) {
    stop("Input is not a serializeJSON-wrapped reference dataset: ",
      conditionMessage(e), call. = FALSE)
  }
)

if (!is.list(dataset) || is.null(dataset$metadata)) {
  stop("Input does not contain a 'metadata' field; not a neoipcr reference dataset.",
    call. = FALSE)
}

neoipcr::write_json(dataset$metadata, file = out_path, pretty = FALSE)
