@server
Feature: Settings and Configuration
    As a user
    I want to configure transcription settings
    So that I can customize the transcription behavior

Scenario: Expand and collapse settings panel
    Given I am on the home page
    When I open the settings panel
    Then the settings panel should be open
    When I close the settings panel
    Then the settings panel should be closed

Scenario: Advanced settings accordion opens
    Given I am on the home page
    And the settings panel is open
    When I open the advanced settings
    Then the advanced settings should be visible

Scenario: Save tuning settings
    Given I am on the home page
    And the settings panel is open
    And the advanced settings are open
    When I save the tuning settings
    And I reload the page
    And I open the settings panel
    And I open the advanced settings
    Then the advanced settings should be visible

Scenario: Reset tuning to defaults
    Given I am on the home page
    And the settings panel is open
    And the advanced settings are open
    When I reset the tuning settings
    Then the advanced settings should be visible

Scenario: Prompt editor accordion opens
    Given I am on the home page
    And the settings panel is open
    When I open the prompt editor
    Then the prompt editor should be visible

Scenario: YouTube button toggles URL input panel
    Given I am on the home page
    When I click the YouTube button
    Then the YouTube URL input should be visible
    When I click the YouTube button
    Then the YouTube URL input should not be visible

@client
Scenario: Server LLM Providers section hidden in standalone client
    Given I am on the home page
    And the settings panel is open
    Then the Server LLM Providers section should not be visible

@client
Scenario: Speed priority toggle is visible in standalone client
    Given I am on the home page
    And the settings panel is open
    Then the speed priority toggle should be visible

@client
Scenario: Session history accordion is present in standalone client
    Given I am on the home page
    And the settings panel is open
    Then the session history accordion should be visible

@client
Scenario: Diagnostics section is accessible in standalone client
    Given I am on the home page
    And the settings panel is open
    When I open the diagnostics section
    Then the diagnostics section should be visible
