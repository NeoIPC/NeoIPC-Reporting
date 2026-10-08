#!/usr/bin/env Rscript
#
# Checks an uploaded validation-exception file with neoipcr's reader before
# the NeoIPC.Reporting service stores it, so that a list the reports cannot
# apply is refused at upload rather than at render time. The stored list
# serves every department, so a list whose records do not name their
# department (DEPARTMENT_CODE) is refused as well.
#
# Usage:
#   Rscript --vanilla check-validation-exceptions.R \
#     --in <staged file> \
#     --name <display name>
#
# Exit status 0 accepts the file. Exit status 3 refuses it, with the reason on
# stdout, the display name standing for the staged path, which means nothing
# to the administrator who uploaded it. Any other status is a failure of the
# check itself, its diagnostics on stderr; R itself exits with 1 on an
# uncaught error and with 2 on a fatal error of its own start-up, so neither
# can pass for a refusal.

args <- commandArgs(trailingOnly = TRUE)
get_arg <- function(flag) {
  i <- match(flag, args)
  if (is.na(i) || i == length(args)) {
    stop("Missing value for ", flag, call. = FALSE)
  }
  args[[i + 1L]]
}

in_path <- get_arg("--in")
display_name <- get_arg("--name")

# neoipcr is loaded as the renders load it: from the editable checkout
# NEOIPCR_DEV_PATH names in a workspace image, else the installed package.
dev_path <- Sys.getenv("NEOIPCR_DEV_PATH", unset = "")
if (nzchar(dev_path)) {
  suppressMessages(pkgload::load_all(dev_path, quiet = TRUE))
} else {
  suppressPackageStartupMessages(library(neoipcr))
}

refuse <- function(reason) {
  cat(gsub(in_path, display_name, reason, fixed = TRUE), "\n", sep = "")
  quit(save = "no", status = 3L)
}

exceptions <- tryCatch(
  neoipcr::read_validation_exceptions(in_path),
  neoipcr_invalid_exception_list = function(cnd) refuse(conditionMessage(cnd)))

if (!"DEPARTMENT_CODE" %in% names(exceptions)) {
  refuse(sprintf(paste(
    "The validation exception file \"%s\" does not name the department of its records.",
    "The stored list serves every department, so each record needs its DEPARTMENT_CODE."),
    display_name))
}
