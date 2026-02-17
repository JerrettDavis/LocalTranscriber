Feature: Transcription Flow
    As a user
    I want to upload audio and get transcription results
    So that I can convert speech to text

@server
Scenario: Upload and transcribe via server with mocked API
    Given I am on the home page
    And the server transcription API is mocked
    When I upload a test audio file
    And I click the transcribe button
    And I wait for transcription to complete
    Then I should be at the "results" stage
    And the result should contain text

@server
Scenario: Results tabs show content
    Given I am on the home page
    And the server transcription API is mocked
    When I upload a test audio file
    And I click the transcribe button
    And I wait for transcription to complete
    Then the "Processed" tab should show content
    And the "Raw" tab should show content
    And the "Logs" tab should show content

@server
Scenario: Reset returns to capture stage
    Given I am on the home page
    And the server transcription API is mocked
    When I upload a test audio file
    And I click the transcribe button
    And I wait for transcription to complete
    And I click the reset button
    Then I should be at the "capture" stage

@server
Scenario: All result tabs show content after transcription
    Given I am on the home page
    And the server transcription API is mocked
    When I upload a test audio file
    And I click the transcribe button
    And I wait for transcription to complete
    Then the "Processed" tab should show content
    And the "Raw" tab should show content
    And the "Tagged" tab should show content
    And the "Logs" tab should show content

@server
Scenario: Server transcription error shows error message
    Given I am on the home page
    And the server transcription API returns an error
    When I upload a test audio file
    And I click the transcribe button
    And I wait for the error state
    Then an error message should be visible

@client @slow
Scenario: Upload and transcribe via WASM client with mocked transcription
    Given I am on the home page
    And the client transcription is mocked
    When I upload a test audio file
    And I click the transcribe button
    And I wait for transcription to complete
    Then I should be at the "results" stage
