# Project agent instructions

## Post-change code review

- After modifying code or implementing a feature, run the `open-code-review-delegate` workflow before handing the work back to the user.
- Use `ocr delegate preview --format json` to identify every changed file, then obtain the applicable rules with `ocr delegate rule --format json <paths...>` and review every listed file.
- Treat the user's request and acceptance criteria as the review background.
- Fix clear critical and high-severity findings that are within the requested scope, then rerun the project's relevant tests and the review. Report medium findings that require a product or architecture decision instead of guessing.
- If OCR cannot run, do not silently skip it: report the exact blocker and still run the repository's normal tests and static checks.
- Documentation-only or configuration-only changes may skip runtime tests when none apply, but they must still be included in the OCR review.
