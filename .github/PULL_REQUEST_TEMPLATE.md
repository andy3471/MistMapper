name: Pull request
description: PR checklist
body:
  - type: checkboxes
    id: checks
    attributes:
      label: Checklist
      options:
        - label: I tested with `dotnet test MistMapper.sln -c Release` (or explain why not)
        - label: I did not commit secrets, PFX files, or publish/ binaries
        - label: This PR stays focused (or the description explains the scope)
  - type: textarea
    id: summary
    attributes:
      label: Summary
      description: What and why
    validations:
      required: true
  - type: textarea
    id: testplan
    attributes:
      label: Test plan
    validations:
      required: true
