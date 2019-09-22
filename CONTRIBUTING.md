# Contribution guidelines

Contributions to this repository are welcome, you can engage with users and contributors on Gitter: 

## Code and documentation contributions:

There is very low bar for submitting a PR for review and discussion, changes to code that will get merged will generally requires a bit of back and forth beyond simplest fixes.

### Contributing to the docs

The best way is to pull the repository and build it, then you can use those FAKE build targets (run a single target with `build.cmd TargetName -st` if you don't want to start from a clean build):

* **GenerateDocs** : Will run FSharp.Formatting over the ./docs/contents folder.
* **ServeDocs** : Will run IIS Express to serve the docs, and then show the home URL with your default browser.

