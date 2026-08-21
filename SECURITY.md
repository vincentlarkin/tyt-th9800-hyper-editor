# Security and safety

Please report security or radio-safety issues privately through GitHub's **Report a vulnerability** feature rather than opening a public issue.

Do not attach radio clone images, channel-plan exports, logs containing personal paths, or other operator-identifying material to public reports. A minimal synthetic reproduction is preferred.

This application performs clone-protocol writes to radio memory. Treat loss of power, cable disconnection, an incorrect model, or an unverified image as potentially capable of leaving the radio in an unusable state. The editor validates the image format, creates a pre-write backup, and verifies a complete read-back, but these safeguards cannot eliminate hardware or driver failures.
